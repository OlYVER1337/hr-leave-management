using LeaveService.Domain.Entities;

namespace LeaveService.Application.Dtos;

public sealed record CreateLeaveRequest(
    int LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason);

public sealed record LeaveDecisionRequest(string? Note);

public sealed record LeaveDecisionCommand(string Decision, string? Note);

public sealed record LeaveRequestHistoryResponse(
    Guid Id,
    Guid ActorId,
    string Action,
    string? FromStatus,
    string ToStatus,
    string? Note,
    DateTime CreatedAtUtc);

// API chi tiết trả request và audit history trong cùng một response.
public sealed record LeaveRequestDetailResponse(
    LeaveRequestResponse Request,
    IReadOnlyList<LeaveRequestHistoryResponse> History);

public sealed record LeaveRequestSummaryResponse(int Pending, int? Approved, int? Total);

public sealed record CancelLeaveRequest(string? Note);

public sealed record LeaveBalanceResponse(
    int LeaveTypeId,
    int Year,
    decimal Entitled,
    decimal Used,
    decimal Pending,
    decimal Available,
    decimal PolicyDays = 0m);

public sealed record LeaveRequestResponse(
    Guid Id,
    Guid EmployeeId,
    Guid? ApproverId,
    int DepartmentId,
    int LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string Reason,
    LeaveRequestStatus Status,
    DateTime CreatedAtUtc,
    IReadOnlyList<LeaveBalanceResponse> Balances);

public sealed record HrEmployeeSnapshot(
    Guid Id,
    string Code,
    string FullName,
    string Role,
    bool IsActive,
    int DepartmentId,
    Guid? ManagerId);

public sealed record HrLeaveType(
    int Id,
    string Code,
    string Name,
    bool IsPaid,
    bool DeductsBalance,
    bool IsActive);

public sealed record HrLeavePolicy(
    int Id,
    int LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    int Year,
    decimal DaysPerYear,
    DateOnly EffectiveFrom = default,
    DateTime CreatedAtUtc = default,
    string Reason = "");

public sealed record HrLeaveCatalog(
    IReadOnlyList<HrLeaveType> LeaveTypes,
    IReadOnlyList<HrLeavePolicy> LeavePolicies);

public sealed record LeaveBalanceEntitlement(int LeaveTypeId, decimal Entitled, int Year = 0);

public sealed record HrHoliday(int Id, DateOnly HolidayDate, string Name);

public sealed record AnnualLeaveAllocation(int Year, decimal Days, decimal Entitled, int? PolicyVersionId = null);
