namespace HrService.Domain.Entities;

public sealed class RolePermissionScope
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public string Scope { get; set; } = string.Empty;

    public RolePermission RolePermission { get; set; } = null!;
    public PermissionScopeRule PermissionScopeRule { get; set; } = null!;
}
