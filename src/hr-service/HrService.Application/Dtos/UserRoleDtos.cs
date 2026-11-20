namespace HrService.Application.Dtos;

public sealed record RoleOptionResponse(
    string Code,
    string Name,
    string? Description,
    bool IsSystem);

public sealed record UserRoleAssignmentResponse(
    string RoleCode,
    string RoleName,
    DateTime GrantedAtUtc,
    DateTime? ExpiresAtUtc,
    Guid? GrantedByEmployeeId);

public sealed record GrantUserRoleRequest(
    string RoleCode,
    DateTimeOffset? ExpiresAtUtc);
