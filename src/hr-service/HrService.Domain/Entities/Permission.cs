namespace HrService.Domain.Entities;

public sealed class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<PermissionScopeRule> ScopeRules { get; set; } = new List<PermissionScopeRule>();
}
