using HrService.Application.Abstractions;
using HrService.Application.Dtos;
using HrService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence.Repositories;

public sealed class AuthorizationRepository(HrDbContext dbContext) : IAuthorizationRepository
{
    public async Task<IReadOnlyList<PermissionScopeGrant>> GetEffectivePermissionScopesAsync(
        Guid employeeId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var grants = await dbContext.UserRoles
            .AsNoTracking()
            .Where(assignment =>
                assignment.EmployeeId == employeeId &&
                assignment.RevokedAtUtc == null &&
                (assignment.ExpiresAtUtc == null || assignment.ExpiresAtUtc > utcNow) &&
                assignment.Employee.IsActive &&
                assignment.Role.IsActive)
            .SelectMany(assignment => assignment.Role.RolePermissions
                .Where(rolePermission => rolePermission.Permission.IsActive)
                .SelectMany(rolePermission => rolePermission.Scopes
                    .Select(scope => new
                    {
                        PermissionCode = rolePermission.Permission.Code,
                        Scope = scope.Scope
                    })))
            .Distinct()
            .OrderBy(grant => grant.PermissionCode)
            .ThenBy(grant => grant.Scope)
            .ToListAsync(cancellationToken);

        return grants
            .Select(grant => new PermissionScopeGrant(grant.PermissionCode, grant.Scope))
            .ToArray();
    }
}
