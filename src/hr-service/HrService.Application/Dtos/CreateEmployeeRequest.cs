namespace HrService.Application.Dtos;

public sealed record CreateEmployeeRequest(
    string Code,
    string FullName,
    string Email,
    string Password,
    string Role,
    DateOnly HireDate,
    int DepartmentId,
    Guid? ManagerId);
