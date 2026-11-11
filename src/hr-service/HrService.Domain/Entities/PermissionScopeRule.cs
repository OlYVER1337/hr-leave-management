namespace HrService.Domain.Entities;

public sealed class PermissionScopeRule
{
    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public string Scope { get; set; } = string.Empty;

    public ICollection<RolePermissionScope> RolePermissionScopes { get; set; } =
        new List<RolePermissionScope>();
}
