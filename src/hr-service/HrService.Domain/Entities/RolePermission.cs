namespace HrService.Domain.Entities;

public sealed class RolePermission
{
    public int RoleId { get; set; }
    public SecurityRole Role { get; set; } = null!;

    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public Guid? GrantedByEmployeeId { get; set; }
    public DateTime GrantedAtUtc { get; set; }

    public ICollection<RolePermissionScope> Scopes { get; set; } = new List<RolePermissionScope>();
}
