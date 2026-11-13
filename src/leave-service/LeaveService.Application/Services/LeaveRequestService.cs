using LeaveService.Application.Abstractions;
using LeaveService.Application.Common;
using LeaveService.Application.Dtos;
using LeaveService.Domain.Entities;

namespace LeaveService.Application.Services;

public sealed class LeaveRequestService(
    ILeaveRequestRepository repository,
    ILeaveHrClient hrClient,
    TimeProvider timeProvider)
{
    private const int MaximumReasonLength = 1000;
    private const int MaximumRangeDays = 366;
    private const int MaximumCalendarRangeDays = 62;

    public async Task<LeaveRequestResponse> CreateAsync(
        Guid employeeId,
        string authorization,
        CreateLeaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        var employee = await hrClient.GetEmployeeAsync(
            employeeId,
            authorization,
            cancellationToken);
        if (!employee.IsActive)
        {
            throw new LeaveConflictException("Inactive employees cannot submit leave requests.");
        }

        var leaveType = await hrClient.GetLeaveTypeAsync(
            request.LeaveTypeId,
            authorization,
            cancellationToken);
        if (!leaveType.IsActive)
        {
            throw new LeaveConflictException("The selected leave type is inactive.");
        }

        var holidays = await hrClient.GetHolidaysAsync(
            request.StartDate,
            request.EndDate,
            authorization,
            cancellationToken);
        var daysByYear = CalculateDaysByYear(request.StartDate, request.EndDate, holidays);
        if (daysByYear.Count == 0)
        {
            throw Invalid("startDate", "The requested period contains no working days.");
        }

        var allocations = new List<AnnualLeaveAllocation>();
        if (leaveType.DeductsBalance)
        {
            foreach (var (year, days) in daysByYear.Where(_ => leaveType.DeductsBalance))
            {
                var policies = await hrClient.GetLeavePoliciesAsync(
                    year,
                    authorization,
                    cancellationToken);
                var policy = policies.FirstOrDefault(item => item.LeaveTypeId == leaveType.Id)
                    ?? throw Invalid(
                        "leaveTypeId",
                        $"No leave policy is configured for leave type '{leaveType.Id}' in {year}.");
                allocations.Add(new AnnualLeaveAllocation(year, days, policy.DaysPerYear));
            }
        }

        var totalDays = daysByYear.Values.Sum();
        var createdAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new LeaveRequest
        {
            EmployeeId = employee.Id,
            ApproverId = employee.ManagerId,
            DepartmentId = employee.DepartmentId,
            LeaveTypeId = leaveType.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            TotalDays = totalDays,
            Reason = request.Reason.Trim(),
            Status = LeaveRequestStatus.Pending,
            CreatedAtUtc = createdAtUtc
        };
        var history = new LeaveRequestHistory
        {
            LeaveRequestId = entity.Id,
            ActorId = employee.Id,
            Action = "Created",
            ToStatus = LeaveRequestStatus.Pending,
            CreatedAtUtc = createdAtUtc
        };

        var (created, balances) = await repository.CreateAsync(
            entity,
            allocations,
            history,
            cancellationToken);
        return ToResponse(created, balances);
    }

    public async Task<IReadOnlyList<LeaveRequestResponse>> GetMineAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var requests = await repository.ListByEmployeeAsync(employeeId, cancellationToken);
        return requests.Select(item => ToResponse(item.Request, item.Balances)).ToArray();
    }

    public async Task<PagedLeaveRequestResponse> GetMinePagedAsync(
        Guid employeeId,
        int page,
        int pageSize,
        string? search,
        string? status,
        int? leaveTypeId,
        CancellationToken cancellationToken = default)
    {
        if (leaveTypeId is <= 0)
            throw Invalid("leaveTypeId", "Leave type ID must be a positive integer.");

        var (rows, totalCount) = await repository.ListByEmployeePageAsync(
            employeeId,
            ParseStatusFilter(status),
            leaveTypeId,
            search,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 100),
            cancellationToken);

        return new PagedLeaveRequestResponse(
            rows.Select(item => ToResponse(item.Request, item.Balances)).ToArray(),
            totalCount);
    }

    public async Task<LeaveRequestResponse> GetVisibleByIdAsync(
        Guid employeeId,
        string authorization,
        Guid requestId,
        LeaveAccess access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.CanRead)
        {
            throw new LeaveForbiddenException("The caller does not have leaves.read permission.");
        }

        var teamEmployeeIds = access.CanReadTeam && !access.CanReadAll
            ? await hrClient.GetTeamEmployeeIdsAsync(employeeId, authorization, cancellationToken)
            : Array.Empty<Guid>();

        var result = await repository.GetVisibleByIdAsync(
            employeeId,
            requestId,
            access,
            teamEmployeeIds,
            cancellationToken);

        if (!result.HasValue)
        {
            // Do not reveal whether a request outside the caller's scope exists.
            throw new LeaveNotFoundException($"Leave request with id '{requestId}' was not found.");
        }

        return ToResponse(result.Value.Request, result.Value.Balances);
    }

    public async Task<IReadOnlyList<LeaveRequestResponse>> GetPendingAsync(
        Guid actorId,
        string authorization,
        LeaveAccess access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (access.CanDecideAll)
        {
            var all = await repository.ListPendingAsync(cancellationToken);
            return all.Select(item => ToResponse(item.Request, item.Balances)).ToArray();
        }

        if (!access.CanDecideTeam)
        {
            throw new LeaveForbiddenException("The caller does not have leaves.decide permission.");
        }

        // Team membership is resolved from HR at request time. ApproverId is only a snapshot
        // and may be stale after an employee is reassigned.
        var teamEmployeeIds = await hrClient.GetTeamEmployeeIdsAsync(
            actorId,
            authorization,
            cancellationToken);
        var requests = await repository.ListPendingByEmployeesAsync(
            teamEmployeeIds,
            cancellationToken);
        return requests.Select(item => ToResponse(item.Request, item.Balances)).ToArray();
    }

    public async Task<PagedLeaveRequestResponse> GetVisiblePagedAsync(
        Guid employeeId,
        string authorization,
        LeaveAccess access,
        int page,
        int pageSize,
        string? search,
        string? status,
        int? leaveTypeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.CanRead)
            throw new LeaveForbiddenException("The caller does not have leaves.read permission.");
        if (leaveTypeId is <= 0)
            throw Invalid("leaveTypeId", "Leave type ID must be a positive integer.");

        var teamEmployeeIds = access.CanReadTeam && !access.CanReadAll
            ? await hrClient.GetTeamEmployeeIdsAsync(employeeId, authorization, cancellationToken)
            : Array.Empty<Guid>();

        var (rows, totalCount) = await repository.ListVisiblePageAsync(
            employeeId,
            access,
            teamEmployeeIds,
            calendarOnly: false,
            from: null,
            to: null,
            ParseStatusFilter(status),
            leaveTypeId,
            search,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 100),
            cancellationToken);

        return new PagedLeaveRequestResponse(
            rows.Select(item => ToResponse(item.Request, item.Balances)).ToArray(),
            totalCount);
    }

    public async Task<PagedLeaveRequestResponse> GetPendingPagedAsync(
        Guid actorId,
        string authorization,
        LeaveAccess access,
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.CanDecide)
            throw new LeaveForbiddenException("The caller does not have leaves.decide permission.");

        var includeAll = access.CanDecideAll;
        var teamEmployeeIds = includeAll
            ? Array.Empty<Guid>()
            : await hrClient.GetTeamEmployeeIdsAsync(actorId, authorization, cancellationToken);

        var (rows, totalCount) = await repository.ListPendingPageAsync(
            teamEmployeeIds,
            includeAll,
            search,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 100),
            cancellationToken);

        return new PagedLeaveRequestResponse(
            rows.Select(item => ToResponse(item.Request, item.Balances)).ToArray(),
            totalCount);
    }

    public async Task<IReadOnlyList<LeaveRequestResponse>> GetVisibleAsync(
        Guid employeeId,
        string authorization,
        LeaveAccess access,
        bool calendarOnly,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.CanRead)
        {
            throw new LeaveForbiddenException("The caller does not have leaves.read permission.");
        }

        if (calendarOnly && (!from.HasValue || !to.HasValue))
        {
            throw Invalid("from", "Calendar queries require both from and to dates.");
        }

        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            throw Invalid("from", "The start date must not be later than the end date.");
        }

        if (calendarOnly &&
            to!.Value.DayNumber - from!.Value.DayNumber + 1 > MaximumCalendarRangeDays)
        {
            throw Invalid("to", $"Calendar queries cannot exceed {MaximumCalendarRangeDays} days.");
        }

        // Team scope is resolved from HR at request time (current manager), not from ApproverId.
        var teamEmployeeIds = access.CanReadTeam && !access.CanReadAll
            ? await hrClient.GetTeamEmployeeIdsAsync(employeeId, authorization, cancellationToken)
            : Array.Empty<Guid>();

        var requests = await repository.ListVisibleAsync(
            employeeId,
            access,
            teamEmployeeIds,
            calendarOnly,
            from,
            to,
            cancellationToken);
        return requests.Select(item => ToResponse(item.Request, item.Balances)).ToArray();
    }

    public async Task<IReadOnlyList<LeaveBalanceResponse>> GetBalancesAsync(
        Guid employeeId,
        int year,
        CancellationToken cancellationToken = default)
    {
        if (year is < 1900 or > 9998)
        {
            throw Invalid("year", "Year must be between 1900 and 9998.");
        }

        var balances = await repository.ListBalancesAsync(employeeId, year, cancellationToken);
        return balances.Select(balance => new LeaveBalanceResponse(
            balance.LeaveTypeId,
            balance.Year,
            balance.Entitled,
            balance.Used,
            balance.Pending,
            balance.Entitled - balance.Used - balance.Pending)).ToArray();
    }

    public async Task<LeaveRequestResponse> DecideAsync(
        Guid actorId,
        string authorization,
        Guid requestId,
        bool approve,
        LeaveAccess access,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.CanDecide)
        {
            throw new LeaveForbiddenException("The caller does not have leaves.decide permission.");
        }

        var request = await repository.GetForDecisionAsync(requestId, cancellationToken)
            ?? throw new LeaveNotFoundException($"Leave request with id '{requestId}' was not found.");
        EnsurePending(request);
        if (request.EmployeeId == actorId)
        {
            throw new LeaveForbiddenException("You cannot approve or reject your own leave request.");
        }

        Guid? verifiedApproverId = null;
        if (!access.CanDecideAll)
        {
            // Team decisions always re-check the employee's current manager in HR.
            // Do not trust the approver snapshot stored on the leave request.
            var employee = await hrClient.GetEmployeeAsync(request.EmployeeId, authorization, cancellationToken);
            if (!employee.IsActive || employee.ManagerId != actorId)
            {
                throw new LeaveForbiddenException(
                    "Only the current active manager can decide a Team request.");
            }

            verifiedApproverId = employee.ManagerId;
        }

        ValidateNote(note);
        var result = await repository.TransitionAsync(
            requestId,
            actorId,
            access,
            approve ? LeaveRequestStatus.Approved : LeaveRequestStatus.Rejected,
            NormalizeNote(note),
            timeProvider.GetUtcNow().UtcDateTime,
            verifiedApproverId,
            cancellationToken);
        return ToResponse(result.Request, result.Balances);
    }

    public async Task<LeaveRequestResponse> CancelAsync(
        Guid employeeId,
        Guid requestId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ValidateNote(note);
        var result = await repository.CancelAsync(
            employeeId,
            requestId,
            NormalizeNote(note),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        return ToResponse(result.Request, result.Balances);
    }

    private static LeaveRequestStatus? ParseStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        if (!Enum.TryParse<LeaveRequestStatus>(status, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw Invalid("status", "Status must be Pending, Approved, Rejected, or Cancelled.");
        }

        return parsed;
    }

    private static void EnsurePending(LeaveRequest request)
    {
        if (request.Status != LeaveRequestStatus.Pending)
        {
            throw new LeaveConflictException("Only pending leave requests can be decided.");
        }
    }

    private static void ValidateNote(string? note)
    {
        if (note?.Trim().Length > MaximumReasonLength)
        {
            throw Invalid("note", $"Note must be at most {MaximumReasonLength} characters.");
        }
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static Dictionary<int, decimal> CalculateDaysByYear(
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<HrHoliday> holidays)
    {
        var holidaysByDate = holidays.Select(item => item.HolidayDate).ToHashSet();
        var result = new Dictionary<int, decimal>();
        var date = startDate;
        while (true)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                !holidaysByDate.Contains(date))
            {
                result[date.Year] = result.GetValueOrDefault(date.Year) + 1m;
            }

            if (date == endDate)
            {
                break;
            }

            date = date.AddDays(1);
        }

        return result;
    }

    private void ValidateRequest(CreateLeaveRequest request, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.LeaveTypeId <= 0)
        {
            errors["leaveTypeId"] = ["Leave type must be a positive integer."];
        }

        if (request.EndDate < request.StartDate)
        {
            errors["endDate"] = ["End date must be on or after start date."];
        }

        if (request.EndDate >= request.StartDate &&
            request.EndDate.DayNumber - request.StartDate.DayNumber + 1 > MaximumRangeDays)
        {
            errors["endDate"] = [$"A leave request cannot span more than {MaximumRangeDays} days."];
        }

        if (request.StartDate < today)
        {
            errors["startDate"] = ["Start date must be today or later."];
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > MaximumReasonLength)
        {
            errors["reason"] = [$"Reason is required and must be at most {MaximumReasonLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new LeaveValidationException(errors);
        }
    }

    private static LeaveValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static LeaveRequestResponse ToResponse(
        LeaveRequest request,
        IReadOnlyList<LeaveBalance> balances) =>
        new(
            request.Id,
            request.EmployeeId,
            request.ApproverId,
            request.DepartmentId,
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate,
            request.TotalDays,
            request.Reason,
            request.Status,
            request.CreatedAtUtc,
            balances.Select(balance => new LeaveBalanceResponse(
                balance.LeaveTypeId,
                balance.Year,
                balance.Entitled,
                balance.Used,
                balance.Pending,
                balance.Entitled - balance.Used - balance.Pending)).ToArray());
}

public sealed record PagedLeaveRequestResponse(IReadOnlyList<LeaveRequestResponse> Items, int TotalCount);
