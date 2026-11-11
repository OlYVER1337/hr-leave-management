using HrService.Domain.Entities;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrService.Tests;

public sealed class UserRoleRepositorySqlTests : IClassFixture<AuthorizationSeederSqlFixture>
{
    private readonly AuthorizationSeederSqlFixture _fixture;

    public UserRoleRepositorySqlTests(AuthorizationSeederSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [HrSqlFact]
    public async Task UserRoleQueries_ReturnActiveCatalogAssignmentsAndOtherAdministratorState()
    {
        var now = new DateTime(2030, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var department = new Department
        {
            Name = $"UserRole repository test {Guid.NewGuid():N}"
        };

        int adminRoleId;
        int targetRoleId;

        await using (var context = _fixture.CreateContext())
        {
            var actor = CreateEmployee(actorId, "USER-ROLE-ACTOR", department);
            var target = CreateEmployee(targetId, "USER-ROLE-TARGET", department);
            context.Employees.AddRange(actor, target);

            var adminRole = CreateRole("RoleAdmin", true);
            var targetRole = CreateRole("RoleTarget", true);
            var disabledRole = CreateRole("RoleDisabled", false);
            context.Roles.AddRange(adminRole, targetRole, disabledRole);

            var rolesManage = new Permission
            {
                Code = "roles.manage",
                Name = "Manage roles",
                IsActive = true
            };
            var employeeRead = new Permission
            {
                Code = "employees.read",
                Name = "Read employees",
                IsActive = true
            };
            var leaveDecide = new Permission
            {
                Code = "leaves.decide",
                Name = "Decide leaves",
                IsActive = true
            };
            context.Permissions.AddRange(rolesManage, employeeRead, leaveDecide);
            await context.SaveChangesAsync();

            adminRoleId = adminRole.Id;
            targetRoleId = targetRole.Id;

            context.PermissionScopeRules.AddRange(
                new PermissionScopeRule { PermissionId = rolesManage.Id, Scope = "All" },
                new PermissionScopeRule { PermissionId = employeeRead.Id, Scope = "Team" },
                new PermissionScopeRule { PermissionId = leaveDecide.Id, Scope = "Team" });

            context.RolePermissions.AddRange(
                new RolePermission
                {
                    RoleId = adminRole.Id,
                    PermissionId = rolesManage.Id,
                    GrantedAtUtc = now
                },
                new RolePermission
                {
                    RoleId = targetRole.Id,
                    PermissionId = employeeRead.Id,
                    GrantedAtUtc = now
                },
                new RolePermission
                {
                    RoleId = targetRole.Id,
                    PermissionId = leaveDecide.Id,
                    GrantedAtUtc = now
                });

            await context.SaveChangesAsync();

            context.RolePermissionScopes.AddRange(
                new RolePermissionScope
                {
                    RoleId = adminRole.Id,
                    PermissionId = rolesManage.Id,
                    Scope = "All"
                },
                new RolePermissionScope
                {
                    RoleId = targetRole.Id,
                    PermissionId = employeeRead.Id,
                    Scope = "Team"
                },
                new RolePermissionScope
                {
                    RoleId = targetRole.Id,
                    PermissionId = leaveDecide.Id,
                    Scope = "Team"
                });

            context.UserRoles.AddRange(
                new UserRole
                {
                    EmployeeId = actorId,
                    RoleId = adminRole.Id,
                    GrantedAtUtc = now
                },
                new UserRole
                {
                    EmployeeId = targetId,
                    RoleId = targetRole.Id,
                    GrantedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(1)
                },
                new UserRole
                {
                    EmployeeId = targetId,
                    RoleId = disabledRole.Id,
                    GrantedAtUtc = now
                });

            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var repository = new UserRoleRepository(context);

            Assert.NotNull(await repository.GetEmployeeByIdAsync(targetId));
            Assert.NotNull(await new EmployeeRepository(context)
                .GetActiveManagerByIdAsync(targetId, now));

            var roles = await repository.ListActiveRolesAsync();
            Assert.Contains(roles, role => role.Id == adminRoleId);
            Assert.Contains(roles, role => role.Id == targetRoleId);
            Assert.DoesNotContain(roles, role => role.Code == "RoleDisabled");

            var targetRole = await repository.GetRoleByCodeAsync("RoleTarget");
            Assert.NotNull(targetRole);
            Assert.Equal(targetRoleId, targetRole.Id);

            var assignments = await repository.ListActiveAssignmentsAsync(targetId, now);
            var assignment = Assert.Single(assignments);
            Assert.Equal("RoleTarget", assignment.Role.Code);

            var unrevoked = await repository.GetUnrevokedAssignmentAsync(targetId, targetRoleId);
            Assert.NotNull(unrevoked);

            var targetGrants = await repository.GetRolePermissionScopesAsync(targetRoleId);
            Assert.Collection(
                targetGrants,
                grant =>
                {
                    Assert.Equal("employees.read", grant.PermissionCode);
                    Assert.Equal("Team", grant.Scope);
                });

            Assert.True(await repository.HasOtherActiveRoleManagersAsync(
                targetId,
                targetRoleId,
                now));
        }

        await using (var context = _fixture.CreateContext())
        {
            var adminAssignment = await context.UserRoles.SingleAsync(
                assignment => assignment.EmployeeId == actorId && assignment.RoleId == adminRoleId);
            adminAssignment.RevokedAtUtc = now;
            var targetAssignment = await context.UserRoles.SingleAsync(
                assignment => assignment.EmployeeId == targetId && assignment.RoleId == targetRoleId);
            targetAssignment.RevokedAtUtc = now;
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var repository = new UserRoleRepository(context);
            Assert.False(await repository.HasOtherActiveRoleManagersAsync(
                targetId,
                targetRoleId,
                now));
            Assert.Null(await new EmployeeRepository(context)
                .GetActiveManagerByIdAsync(targetId, now));
        }
    }

    private static Employee CreateEmployee(Guid id, string code, Department department) =>
        new()
        {
            Id = id,
            Code = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@example.test",
            PasswordHash = "integration-test-placeholder",
            Role = Role.Employee,
            HireDate = new DateOnly(2026, 1, 1),
            IsActive = true,
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
}
