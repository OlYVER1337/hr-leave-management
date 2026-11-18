using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence;

public class HrDbContext(DbContextOptions<HrDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<SecurityRole> Roles => Set<SecurityRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<PermissionScopeRule> PermissionScopeRules => Set<PermissionScopeRule>();
    public DbSet<RolePermissionScope> RolePermissionScopes => Set<RolePermissionScope>();
    public DbSet<AuthzAuditLog> AuthzAuditLogs => Set<AuthzAuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Department>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Employee>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.Department).WithMany(d => d.Employees)
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Manager).WithMany(m => m.Subordinates)
                .HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.EmployeeId, x.RevokedAtUtc, x.ExpiresAtUtc });
            e.HasOne(x => x.Employee).WithMany()
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LeaveType>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        b.Entity<LeavePolicy>(e =>
        {
            e.Property(x => x.DaysPerYear).HasPrecision(5, 1);
            e.HasIndex(x => new { x.LeaveTypeId, x.Year }).IsUnique();
            e.HasOne(x => x.LeaveType).WithMany(t => t.Policies)
                .HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Holiday>(e =>
        {
            e.HasIndex(x => x.HolidayDate).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        });

        b.Entity<SecurityRole>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(250);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<Permission>(e =>
        {
            e.ToTable("Permissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(250);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<UserRole>(e =>
        {
            e.ToTable("UserRoles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.EmployeeId, x.RoleId })
                .IsUnique()
                .HasFilter("[RevokedAtUtc] IS NULL");
            e.HasIndex(x => new { x.EmployeeId, x.RevokedAtUtc, x.ExpiresAtUtc });
            e.HasIndex(x => x.RoleId);

            e.HasOne(x => x.Employee).WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Role).WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany()
                .HasForeignKey(x => x.GrantedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany()
                .HasForeignKey(x => x.RevokedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.Property(x => x.GrantedAtUtc).IsRequired();

            e.HasOne(x => x.Role).WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Permission).WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany()
                .HasForeignKey(x => x.GrantedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PermissionScopeRule>(e =>
        {
            e.ToTable("PermissionScopeRules", table =>
                table.HasCheckConstraint(
                    "CK_PermissionScopeRules_Scope",
                    "[Scope] IN (N'Self', N'Team', N'Department', N'All')"));

            e.HasKey(x => new { x.PermissionId, x.Scope });
            e.Property(x => x.Scope).HasMaxLength(20).IsRequired();

            e.HasOne(x => x.Permission).WithMany(x => x.ScopeRules)
                .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RolePermissionScope>(e =>
        {
            e.ToTable("RolePermissionScopes", table =>
                table.HasCheckConstraint(
                    "CK_RolePermissionScopes_Scope",
                    "[Scope] IN (N'Self', N'Team', N'Department', N'All')"));

            e.HasKey(x => new { x.RoleId, x.PermissionId, x.Scope });
            e.Property(x => x.Scope).HasMaxLength(20).IsRequired();

            e.HasOne(x => x.RolePermission).WithMany(x => x.Scopes)
                .HasForeignKey(x => new { x.RoleId, x.PermissionId })
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.PermissionScopeRule).WithMany(x => x.RolePermissionScopes)
                .HasForeignKey(x => new { x.PermissionId, x.Scope })
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AuthzAuditLog>(e =>
        {
            e.ToTable("AuthzAuditLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Action).HasMaxLength(80).IsRequired();
            e.Property(x => x.TargetType).HasMaxLength(80).IsRequired();
            e.Property(x => x.TargetId).HasMaxLength(100);
            e.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.AfterJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => new { x.TargetEmployeeId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.ActorEmployeeId, x.CreatedAtUtc });

            e.HasOne<Employee>().WithMany()
                .HasForeignKey(x => x.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany()
                .HasForeignKey(x => x.TargetEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
