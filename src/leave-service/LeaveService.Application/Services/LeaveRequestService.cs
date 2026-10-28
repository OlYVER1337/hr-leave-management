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

    public async Task<LeaveRequestResponse> GetByIdAsync(
        Guid employeeId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var result = await repository.GetByIdAsync(employeeId, requestId, cancellationToken)
            ?? throw new LeaveNotFoundException($"Leave request with id '{requestId}' was not found.");
        return ToResponse(result.Request, result.Balances);
    }

    public async Task<IReadOnlyList<LeaveRequestResponse>> GetPendingAsync(
        Guid actorId,
        bool isHr,
        CancellationToken cancellationToken = default)
    {
        var requests = await repository.ListPendingAsync(
            isHr ? null : actorId,
            cancellationToken);
        return requests.Select(item => ToResponse(item.Request, item.Balances)).ToArray();
    }

    public async Task<IReadOnlyList<LeaveRequestResponse>> GetVisibleAsync(
        Guid employeeId,
        bool isHr,
        bool calendarOnly,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            throw Invalid("from", "The start date must not be later than the end date.");
        }

        var requests = await repository.ListVisibleAsync(
            employeeId,
            isHr,
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
        bool isHr,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var request = await repository.GetForDecisionAsync(requestId, cancellationToken)
            ?? throw new LeaveNotFoundException($"Leave request with id '{requestId}' was not found.");
        EnsurePending(request);
        if (!isHr)
        {
            if (request.ApproverId != actorId)
            {
                throw new LeaveForbiddenException("Only the assigned manager or HR can decide this request.");
            }

            await hrClient.GetEmployeeAsync(request.EmployeeId, authorization, cancellationToken);
        }

        ValidateNote(note);
        var result = await repository.TransitionAsync(
            requestId,
            actorId,
            isHr,
            approve ? LeaveRequestStatus.Approved : LeaveRequestStatus.Rejected,
            NormalizeNote(note),
            timeProvider.GetUtcNow().UtcDateTime,
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
