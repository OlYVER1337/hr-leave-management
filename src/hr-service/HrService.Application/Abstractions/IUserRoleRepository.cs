using HrService.Application.Dtos;
using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IUserRoleRepository
{
    Task<Employee?> GetEmployeeByIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SecurityRole>> ListActiveRolesAsync(
        CancellationToken cancellationToken = default);

    Task<SecurityRole?> GetRoleByCodeAsync(
        string roleCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserRole>> ListActiveAssignmentsAsync(
        Guid employeeId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<UserRole?> GetUnrevokedAssignmentAsync(
        Guid employeeId,
        int roleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionScopeGrant>> GetRolePermissionScopesAsync(
        int roleId,
        CancellationToken cancellationToken = default);

    Task<bool> HasOtherActiveRoleManagersAsync(
        Guid excludedEmployeeId,
        int excludedRoleId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task BeginSerializableTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    void Add(UserRole assignment);
    void Add(AuthzAuditLog auditLog);
}
