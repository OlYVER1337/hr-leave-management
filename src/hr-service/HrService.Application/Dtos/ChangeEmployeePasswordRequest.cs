namespace HrService.Application.Dtos;

public sealed record ChangeEmployeePasswordRequest(
    string CurrentPassword,
    string NewPassword);
