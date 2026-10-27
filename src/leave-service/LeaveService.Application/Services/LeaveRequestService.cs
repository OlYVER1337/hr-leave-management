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
        ValidateRequest(request);
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
            foreach (var (year, days) in daysByYear)
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

    private static void ValidateRequest(CreateLeaveRequest request)
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
