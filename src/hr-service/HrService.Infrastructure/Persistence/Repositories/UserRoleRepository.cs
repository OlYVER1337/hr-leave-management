using System.Data;
using HrService.Application.Abstractions;
using HrService.Application.Dtos;
using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HrService.Infrastructure.Persistence.Repositories;

public sealed class UserRoleRepository(HrDbContext dbContext) : IUserRoleRepository
{
    private IDbContextTransaction? _transaction;

    public async Task BeginSerializableTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A UserRoles transaction is already active.");
        }

        _transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    public async Task CommitTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = _transaction
            ?? throw new InvalidOperationException("No UserRoles transaction is active.");

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = _transaction;
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public Task<Employee?> GetEmployeeByIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken);
    }

    public async Task<IReadOnlyList<SecurityRole>> ListActiveRolesAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Roles
            .AsNoTracking()
            .Where(role => role.IsActive)
            .OrderBy(role => role.Code)
            .ToListAsync(cancellationToken);
    }

    public Task<SecurityRole?> GetRoleByCodeAsync(
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoleCode = roleCode.Trim().ToUpperInvariant();
        return dbContext.Roles
            .FirstOrDefaultAsync(
                role => role.Code.ToUpper() == normalizedRoleCode,
                cancellationToken);
    }

    public async Task<IReadOnlyList<UserRole>> ListActiveAssignmentsAsync(
        Guid employeeId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.UserRoles
            .AsNoTracking()
            .Include(assignment => assignment.Role)
            .Where(assignment =>
                assignment.EmployeeId == employeeId &&
                assignment.Employee.IsActive &&
                assignment.RevokedAtUtc == null &&
                (assignment.ExpiresAtUtc == null || assignment.ExpiresAtUtc > utcNow) &&
                assignment.Role.IsActive)
            .OrderBy(assignment => assignment.Role.Code)
            .ToListAsync(cancellationToken);
    }

    public Task<UserRole?> GetUnrevokedAssignmentAsync(
        Guid employeeId,
        int roleId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.UserRoles
            .Include(assignment => assignment.Role)
            .FirstOrDefaultAsync(
                assignment =>
                    assignment.EmployeeId == employeeId &&
                    assignment.RoleId == roleId &&
                    assignment.RevokedAtUtc == null,
                cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionScopeGrant>> GetRolePermissionScopesAsync(
        int roleId,
        CancellationToken cancellationToken = default)
    {
        var grants = await dbContext.RolePermissions
            .AsNoTracking()
            .Where(grant =>
                grant.RoleId == roleId &&
                grant.Permission.IsActive)
            .SelectMany(grant => grant.Scopes.Select(scope => new
            {
                PermissionCode = grant.Permission.Code,
                Scope = scope.Scope
            }))
            .OrderBy(grant => grant.PermissionCode)
            .ThenBy(grant => grant.Scope)
            .ToListAsync(cancellationToken);

        return grants
            .Select(grant => new PermissionScopeGrant(grant.PermissionCode, grant.Scope))
            .ToArray();
    }

    public Task<bool> HasOtherActiveRoleManagersAsync(
        Guid excludedEmployeeId,
        int excludedRoleId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        return dbContext.UserRoles
            .AsNoTracking()
            .AnyAsync(assignment =>
                (assignment.EmployeeId != excludedEmployeeId ||
                 assignment.RoleId != excludedRoleId) &&
                assignment.RevokedAtUtc == null &&
                (assignment.ExpiresAtUtc == null || assignment.ExpiresAtUtc > utcNow) &&
                assignment.Employee.IsActive &&
                assignment.Role.IsActive &&
                assignment.Role.RolePermissions.Any(grant =>
                    grant.Permission.IsActive &&
                    grant.Permission.Code == "roles.manage" &&
                    grant.Scopes.Any(scope => scope.Scope == "All")),
                cancellationToken);
    }

    public void Add(UserRole assignment)
    {
        dbContext.UserRoles.Add(assignment);
    }

    public void Add(AuthzAuditLog auditLog)
    {
        dbContext.AuthzAuditLogs.Add(auditLog);
    }
}
