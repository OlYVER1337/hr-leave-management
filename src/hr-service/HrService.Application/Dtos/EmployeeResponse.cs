namespace HrService.Application.Dtos;

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
