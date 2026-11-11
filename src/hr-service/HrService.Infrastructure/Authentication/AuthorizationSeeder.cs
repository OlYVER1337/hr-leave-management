using System.Data;
using HrService.Domain.Entities;
using HrService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrService.Infrastructure.Authentication;

/// <summary>
/// Seeds the V1 authorization catalog and performs a one-time-compatible
/// backfill from the legacy Employee.Role column to UserRoles.
/// </summary>
public sealed class AuthorizationSeeder(
    HrDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<AuthorizationSeeder> logger)
{
    private static readonly PermissionSeed[] PermissionSeeds =
    [
        new("employees.read", "Xem nhân viên", "Xem hồ sơ nhân viên trong phạm vi được cấp.", ["Self", "Team", "All"]),
        new("employees.create", "Tạo nhân viên", "Tạo hồ sơ nhân viên.", ["All"]),
        new("employees.update", "Cập nhật nhân viên", "Cập nhật hồ sơ nhân viên.", ["All"]),
        new("employees.deactivate", "Ngừng kích hoạt nhân viên", "Ngừng kích hoạt tài khoản nhân viên.", ["All"]),
        new("employees.password.reset", "Đặt lại mật khẩu", "Đặt lại mật khẩu cho nhân viên.", ["All"]),
        new("leaves.read", "Xem đơn nghỉ", "Xem đơn nghỉ trong phạm vi được cấp.", ["Self", "Team", "All"]),
        new("leaves.create", "Tạo đơn nghỉ", "Tạo đơn nghỉ cho bản thân.", ["Self"]),
        new("leaves.decide", "Quyết định đơn nghỉ", "Duyệt hoặc từ chối đơn nghỉ.", ["Team", "All"]),
        new("leaves.cancel", "Hủy đơn nghỉ", "Hủy đơn nghỉ của bản thân khi còn hợp lệ.", ["Self"]),
        new("roles.manage", "Quản lý vai trò", "Quản lý gán vai trò và quyền trong hệ thống.", ["All"])
    ];

    private static readonly RoleSeed[] RoleSeeds =
    [
        new(
            "Employee",
            "Nhân viên",
            "Nhân viên thông thường.",
            [
                new("employees.read", ["Self"]),
                new("leaves.read", ["Self"]),
                new("leaves.create", ["Self"]),
                new("leaves.cancel", ["Self"])
            ]),
        new(
            "Manager",
            "Quản lý",
            "Quản lý trực tiếp nhân viên.",
            [
                new("employees.read", ["Self", "Team"]),
                new("leaves.read", ["Self", "Team"]),
                new("leaves.create", ["Self"]),
                new("leaves.cancel", ["Self"]),
                new("leaves.decide", ["Team"])
            ]),
        new(
            "HR",
            "Nhân sự",
            "Quản trị nghiệp vụ nhân sự và đơn nghỉ.",
            [
                new("employees.read", ["All"]),
                new("employees.create", ["All"]),
                new("employees.update", ["All"]),
                new("employees.deactivate", ["All"]),
                new("employees.password.reset", ["All"]),
                new("leaves.read", ["All"]),
                new("leaves.create", ["Self"]),
                new("leaves.decide", ["All"]),
                new("leaves.cancel", ["Self"]),
                new("roles.manage", ["All"])
            ]),
        new(
            "Auditor",
            "Kiểm toán",
            "Chỉ đọc dữ liệu nhân viên và đơn nghỉ.",
            [
                new("employees.read", ["All"]),
                new("leaves.read", ["All"])
            ])
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var roles = await EnsureRolesAsync(cancellationToken);
        var permissions = await EnsurePermissionsAsync(cancellationToken);

        await EnsurePermissionScopeRulesAsync(permissions, cancellationToken);
        await EnsureRolePermissionsAsync(roles, permissions, now, cancellationToken);
        await BackfillUserRolesAsync(roles, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Authorization baseline seeded. Roles: {RoleCount}; permissions: {PermissionCount}.",
            RoleSeeds.Length,
            PermissionSeeds.Length);
    }

    private async Task<Dictionary<string, SecurityRole>> EnsureRolesAsync(
        CancellationToken cancellationToken)
    {
        var existingRoles = await dbContext.Roles.ToListAsync(cancellationToken);
        var rolesByCode = existingRoles.ToDictionary(role => role.Code, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var seed in RoleSeeds)
        {
            if (rolesByCode.ContainsKey(seed.Code))
            {
                continue;
            }

            var role = new SecurityRole
            {
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                IsSystem = true,
                IsActive = true,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };

            dbContext.Roles.Add(role);
            rolesByCode.Add(seed.Code, role);
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return rolesByCode;
    }

    private async Task<Dictionary<string, Permission>> EnsurePermissionsAsync(
        CancellationToken cancellationToken)
    {
        var existingPermissions = await dbContext.Permissions.ToListAsync(cancellationToken);
        var permissionsByCode = existingPermissions.ToDictionary(
            permission => permission.Code,
            StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var seed in PermissionSeeds)
        {
            if (permissionsByCode.ContainsKey(seed.Code))
            {
                continue;
            }

            var permission = new Permission
            {
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                IsActive = true
            };

            dbContext.Permissions.Add(permission);
            permissionsByCode.Add(seed.Code, permission);
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return permissionsByCode;
    }

    private async Task EnsurePermissionScopeRulesAsync(
        IReadOnlyDictionary<string, Permission> permissions,
        CancellationToken cancellationToken)
    {
        var existingRules = await dbContext.PermissionScopeRules
            .Select(rule => new { rule.PermissionId, rule.Scope })
            .ToListAsync(cancellationToken);

        var existingKeys = existingRules
            .Select(rule => (rule.PermissionId, rule.Scope))
            .ToHashSet();

        foreach (var seed in PermissionSeeds)
        {
            var permission = permissions[seed.Code];

            foreach (var scope in seed.AllowedScopes)
            {
                if (!existingKeys.Add((permission.Id, scope)))
                {
                    continue;
                }

                dbContext.PermissionScopeRules.Add(new PermissionScopeRule
                {
                    PermissionId = permission.Id,
                    Scope = scope
                });
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureRolePermissionsAsync(
        IReadOnlyDictionary<string, SecurityRole> roles,
        IReadOnlyDictionary<string, Permission> permissions,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingGrants = await dbContext.RolePermissions
            .Select(grant => new { grant.RoleId, grant.PermissionId })
            .ToListAsync(cancellationToken);

        var grantKeys = existingGrants
            .Select(grant => (grant.RoleId, grant.PermissionId))
            .ToHashSet();

        foreach (var roleSeed in RoleSeeds)
        {
            var role = roles[roleSeed.Code];

            foreach (var grantSeed in roleSeed.Grants)
            {
                var permission = permissions[grantSeed.PermissionCode];

                if (grantKeys.Add((role.Id, permission.Id)))
                {
                    dbContext.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = permission.Id,
                        GrantedAtUtc = now
                    });
                }
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var existingScopes = await dbContext.RolePermissionScopes
            .Select(scope => new { scope.RoleId, scope.PermissionId, scope.Scope })
            .ToListAsync(cancellationToken);

        var scopeKeys = existingScopes
            .Select(scope => (scope.RoleId, scope.PermissionId, scope.Scope))
            .ToHashSet();

        foreach (var roleSeed in RoleSeeds)
        {
            var role = roles[roleSeed.Code];

            foreach (var grantSeed in roleSeed.Grants)
            {
                var permission = permissions[grantSeed.PermissionCode];

                foreach (var scope in grantSeed.Scopes)
                {
                    if (!scopeKeys.Add((role.Id, permission.Id, scope)))
                    {
                        continue;
                    }

                    dbContext.RolePermissionScopes.Add(new RolePermissionScope
                    {
                        RoleId = role.Id,
                        PermissionId = permission.Id,
                        Scope = scope
                    });
                }
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task BackfillUserRolesAsync(
        IReadOnlyDictionary<string, SecurityRole> roles,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var employees = await dbContext.Employees
            .Include(employee => employee.UserRoles)
            .ToListAsync(cancellationToken);

        var addedCount = 0;

        foreach (var employee in employees)
        {
            var legacyRoleCode = employee.Role.ToString();

            if (!roles.TryGetValue(legacyRoleCode, out var role))
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy Role '{legacyRoleCode}' tương ứng với Employee.Role của nhân viên {employee.Id}.");
            }

            // Any existing assignment (including revoked/expired history) means
            // this employee-role pair has already been handled. Do not silently
            // restore a role that an administrator intentionally revoked.
            if (employee.UserRoles.Any(assignment => assignment.RoleId == role.Id))
            {
                continue;
            }

            dbContext.UserRoles.Add(new UserRole
            {
                EmployeeId = employee.Id,
                RoleId = role.Id,
                GrantedAtUtc = now
            });
            addedCount++;
        }

        if (addedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Authorization backfill checked {EmployeeCount} employees and created {AssignmentCount} legacy role assignments.",
            employees.Count,
            addedCount);
    }

    private sealed record PermissionSeed(
        string Code,
        string Name,
        string Description,
        string[] AllowedScopes);

    private sealed record RoleSeed(
        string Code,
        string Name,
        string Description,
        RolePermissionSeed[] Grants);

    private sealed record RolePermissionSeed(
        string PermissionCode,
        string[] Scopes);
}
