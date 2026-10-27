namespace HrService.Application.Dtos;

public sealed record UpdateEmployeeRequest(
    string Code,
    string FullName,
    string Email,
    string Role,
    DateOnly HireDate,
    int DepartmentId,
    Guid? ManagerId);
