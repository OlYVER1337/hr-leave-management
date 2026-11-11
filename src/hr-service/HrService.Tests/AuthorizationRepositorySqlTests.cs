using HrService.Application.Dtos;
using HrService.Domain.Entities;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrService.Tests;

public sealed class AuthorizationRepositorySqlTests : IClassFixture<AuthorizationSeederSqlFixture>
{
    private readonly AuthorizationSeederSqlFixture _fixture;

    public AuthorizationRepositorySqlTests(AuthorizationSeederSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [HrSqlFact]
    public async Task GetEffectivePermissionScopesAsync_UsesOnlyActiveUnexpiredAssignmentsAndGrants()
    {
        var now = new DateTime(2030, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var employeeId = Guid.NewGuid();
        var inactiveEmployeeId = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            var department = new Department
            {
                Name = $"Authorization repository test {Guid.NewGuid():N}"
            };
            context.Departments.Add(department);

            var employee = CreateEmployee(employeeId, "AUTH-REPO-ACTIVE", true, department);
            var inactiveEmployee = CreateEmployee(inactiveEmployeeId, "AUTH-REPO-INACTIVE", false, department);
            context.Employees.AddRange(employee, inactiveEmployee);

            var activeRoleA = CreateRole("RepoActiveA", true);
            var activeRoleB = CreateRole("RepoActiveB", true);
            var expiredRole = CreateRole("RepoExpired", true);
            var revokedRole = CreateRole("RepoRevoked", true);
            var inactiveRole = CreateRole("RepoDisabled", false);
            context.Roles.AddRange(activeRoleA, activeRoleB, expiredRole, revokedRole, inactiveRole);

            var readPermission = CreatePermission("employees.read", true);
            var expiredPermission = CreatePermission("test.expired", true);
            var revokedPermission = CreatePermission("test.revoked", true);
            var inactiveRolePermission = CreatePermission("test.inactive-role", true);
            var inactivePermission = CreatePermission("test.inactive-permission", false);
            context.Permissions.AddRange(
                readPermission,
                expiredPermission,
                revokedPermission,
                inactiveRolePermission,
                inactivePermission);

            await context.SaveChangesAsync();

            context.PermissionScopeRules.AddRange(
                new PermissionScopeRule { PermissionId = readPermission.Id, Scope = "Self" },
                new PermissionScopeRule { PermissionId = readPermission.Id, Scope = "Team" },
                new PermissionScopeRule { PermissionId = expiredPermission.Id, Scope = "All" },
                new PermissionScopeRule { PermissionId = revokedPermission.Id, Scope = "Team" },
                new PermissionScopeRule { PermissionId = inactiveRolePermission.Id, Scope = "All" },
                new PermissionScopeRule { PermissionId = inactivePermission.Id, Scope = "All" });

            var activeGrantA = new RolePermission
            {
                RoleId = activeRoleA.Id,
                PermissionId = readPermission.Id,
                GrantedAtUtc = now
            };
            var activeGrantB = new RolePermission
            {
                RoleId = activeRoleB.Id,
                PermissionId = readPermission.Id,
                GrantedAtUtc = now
            };
            var expiredGrant = new RolePermission
            {
                RoleId = expiredRole.Id,
                PermissionId = expiredPermission.Id,
                GrantedAtUtc = now
            };
            var revokedGrant = new RolePermission
            {
                RoleId = revokedRole.Id,
                PermissionId = revokedPermission.Id,
                GrantedAtUtc = now
            };
            var inactiveRoleGrant = new RolePermission
            {
                RoleId = inactiveRole.Id,
                PermissionId = inactiveRolePermission.Id,
                GrantedAtUtc = now
            };
            var inactivePermissionGrant = new RolePermission
            {
                RoleId = activeRoleA.Id,
                PermissionId = inactivePermission.Id,
                GrantedAtUtc = now
            };

            context.RolePermissions.AddRange(
                activeGrantA,
                activeGrantB,
                expiredGrant,
                revokedGrant,
                inactiveRoleGrant,
                inactivePermissionGrant);

            await context.SaveChangesAsync();

            context.RolePermissionScopes.AddRange(
                new RolePermissionScope
                {
                    RoleId = activeGrantA.RoleId,
                    PermissionId = activeGrantA.PermissionId,
                    Scope = "Self"
                },
                new RolePermissionScope
                {
                    RoleId = activeGrantA.RoleId,
                    PermissionId = activeGrantA.PermissionId,
                    Scope = "Team"
                },
                new RolePermissionScope
                {
                    RoleId = activeGrantB.RoleId,
                    PermissionId = activeGrantB.PermissionId,
                    Scope = "Team"
                },
                new RolePermissionScope
                {
                    RoleId = expiredGrant.RoleId,
                    PermissionId = expiredGrant.PermissionId,
                    Scope = "All"
                },
                new RolePermissionScope
                {
                    RoleId = revokedGrant.RoleId,
                    PermissionId = revokedGrant.PermissionId,
                    Scope = "Team"
                },
                new RolePermissionScope
                {
                    RoleId = inactiveRoleGrant.RoleId,
                    PermissionId = inactiveRoleGrant.PermissionId,
                    Scope = "All"
                },
                new RolePermissionScope
                {
                    RoleId = inactivePermissionGrant.RoleId,
                    PermissionId = inactivePermissionGrant.PermissionId,
                    Scope = "All"
                });

            await context.SaveChangesAsync();

            context.UserRoles.AddRange(
                new UserRole
                {
                    EmployeeId = employeeId,
                    RoleId = activeRoleA.Id,
                    GrantedAtUtc = now
                },
                new UserRole
                {
                    EmployeeId = employeeId,
                    RoleId = activeRoleB.Id,
                    GrantedAtUtc = now
                },
                new UserRole
                {
                    EmployeeId = employeeId,
                    RoleId = expiredRole.Id,
                    GrantedAtUtc = now.AddDays(-2),
                    ExpiresAtUtc = now.AddMinutes(-1)
                },
                new UserRole
                {
                    EmployeeId = employeeId,
                    RoleId = revokedRole.Id,
                    GrantedAtUtc = now.AddDays(-2),
                    RevokedAtUtc = now.AddMinutes(-1)
                },
                new UserRole
                {
                    EmployeeId = employeeId,
                    RoleId = inactiveRole.Id,
                    GrantedAtUtc = now
                },
                new UserRole
                {
                    EmployeeId = inactiveEmployeeId,
                    RoleId = activeRoleA.Id,
                    GrantedAtUtc = now
                });

            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var repository = new AuthorizationRepository(context);

            var activeGrants = await repository.GetEffectivePermissionScopesAsync(
                employeeId,
                now);

            Assert.Equal(
                new[]
                {
                    new PermissionScopeGrant("employees.read", "Self"),
                    new PermissionScopeGrant("employees.read", "Team")
                },
                activeGrants);

            var inactiveEmployeeGrants = await repository.GetEffectivePermissionScopesAsync(
                inactiveEmployeeId,
                now);

            Assert.Empty(inactiveEmployeeGrants);
        }
    }

    private static Employee CreateEmployee(
        Guid id,
        string code,
        bool isActive,
        Department department) =>
        new()
        {
            Id = id,
            Code = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@example.test",
            PasswordHash = "integration-test-placeholder",
            Role = Role.Employee,
            HireDate = new DateOnly(2026, 1, 1),
            IsActive = isActive,
            Department = department
        };

    private static SecurityRole CreateRole(string code, bool isActive) =>
        new()
        {
            Code = code,
            Name = code,
            IsSystem = false,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static Permission CreatePermission(string code, bool isActive) =>
        new()
        {
            Code = code,
            Name = code,
            IsActive = isActive
        };
}
