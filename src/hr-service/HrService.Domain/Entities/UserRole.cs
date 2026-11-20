namespace HrService.Domain.Entities;

public sealed class UserRole
{
    public long Id { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public int RoleId { get; set; }
    public SecurityRole Role { get; set; } = null!;

    public Guid? GrantedByEmployeeId { get; set; }
    public DateTime GrantedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public Guid? RevokedByEmployeeId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
