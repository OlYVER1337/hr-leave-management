namespace HrService.Application.Dtos;

public sealed record DepartmentRequest(string Name);

public sealed record DepartmentResponse(int Id, string Name);

public sealed record CreateLeaveTypeRequest(
    string Code,
    string Name,
    bool IsPaid,
    bool DeductsBalance);

public sealed record UpdateLeaveTypeRequest(
    string Name,
    bool IsPaid,
    bool DeductsBalance,
    bool IsActive);

public sealed record LeaveTypeResponse(
    int Id,
    string Code,
    string Name,
    bool IsPaid,
    bool DeductsBalance,
    bool IsActive);

public sealed record LeavePolicyRequest(
    int LeaveTypeId,
    int Year,
    decimal DaysPerYear);

public sealed record LeavePolicyResponse(
    int Id,
    int LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    int Year,
    decimal DaysPerYear);

public sealed record HolidayRequest(DateOnly HolidayDate, string Name);

public sealed record HolidayResponse(int Id, DateOnly HolidayDate, string Name);

public sealed record EmployeeLookupResponse(
    Guid Id,
    string Code,
    string FullName,
    string Role,
    bool IsActive,
    int DepartmentId,
    Guid? ManagerId);
