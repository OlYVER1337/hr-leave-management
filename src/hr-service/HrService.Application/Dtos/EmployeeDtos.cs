namespace HrService.Application.Dtos;

public sealed record ChangeEmployeePasswordRequest(
    string CurrentPassword,
    string NewPassword);

public sealed record CreateEmployeeRequest(
    string Code,
    string FullName,
    string Email,
    string Password,
    string Role,
    DateOnly HireDate,
    int DepartmentId,
    Guid? ManagerId);

public sealed record EmployeeResponse(
    Guid Id,
    string Code,
    string FullName,
    string Email,
    string Role,
    DateOnly HireDate,
    bool IsActive,
    int DepartmentId,
    string DepartmentName,
    Guid? ManagerId,
    string? ManagerName);

public sealed record ResetEmployeePasswordRequest(string NewPassword);

public sealed record UpdateEmployeeRequest(
    string Code,
    string FullName,
    string Email,
    string Role,
    DateOnly HireDate,
    int DepartmentId,
    Guid? ManagerId);
