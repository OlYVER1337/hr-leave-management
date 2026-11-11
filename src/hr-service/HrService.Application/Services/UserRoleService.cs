using System.Text.Json;
using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Domain.Entities;

namespace HrService.Application.Services;

public sealed class UserRoleService(
    IUserRoleRepository userRoleRepository,
    AuthorizationService authorizationService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RoleOptionResponse>> GetAvailableRolesAsync(
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        await RequireRoleManagementAsync(actorId, cancellationToken);

        var roles = await userRoleRepository.ListActiveRolesAsync(cancellationToken);
        return roles
            .Select(role => new RoleOptionResponse(
                role.Code,
                role.Name,
                role.Description,
                role.IsSystem))
            .ToArray();
    }

    public async Task<IReadOnlyList<UserRoleAssignmentResponse>> GetEmployeeRolesAsync(
        Guid actorId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        await RequireRoleManagementAsync(actorId, cancellationToken);
        await RequireEmployeeAsync(employeeId, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var assignments = await userRoleRepository.ListActiveAssignmentsAsync(
            employeeId,
            now,
            cancellationToken);

        return assignments.Select(ToResponse).ToArray();
    }

    public async Task<UserRoleAssignmentResponse> GrantAsync(
        Guid actorId,
        Guid employeeId,
        GrantUserRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorAuthorization = await RequireRoleManagementAsync(actorId, cancellationToken);
        EnsureNotSelf(actorId, employeeId);

        var employee = await RequireEmployeeAsync(employeeId, cancellationToken);
        if (!employee.IsActive)
        {
            throw new ConflictException("Cannot assign a role to an inactive employee.");
        }

        var roleCode = request.RoleCode?.Trim();
        if (string.IsNullOrWhiteSpace(roleCode) || roleCode.Length > 50)
        {
            throw ValidationError(
                "roleCode",
                "Role code is required and cannot exceed 50 characters.");
        }

        var role = await userRoleRepository.GetRoleByCodeAsync(roleCode, cancellationToken);
        if (role is null || !role.IsActive)
        {
            throw new NotFoundException($"Active role '{roleCode}' was not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime? expiresAtUtc = request.ExpiresAtUtc?.UtcDateTime;
        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= now)
        {
            throw ValidationError("expiresAtUtc", "Expiration must be later than the current UTC time.");
        }

        var targetGrants = await userRoleRepository.GetRolePermissionScopesAsync(
            role.Id,
            cancellationToken);
        EnsureGrantDoesNotEscalate(actorAuthorization, targetGrants);

        var grantsRoleManagement = targetGrants.Any(grant =>
            string.Equals(grant.PermissionCode, "roles.manage", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(grant.Scope, "All", StringComparison.OrdinalIgnoreCase));

        // An administrator assignment must not expire automatically and leave
        // the system without an account able to manage roles.
        if (grantsRoleManagement && expiresAtUtc.HasValue)
        {
            throw ValidationError(
                "expiresAtUtc",
                "A role that grants roles.manage with All scope cannot have an expiration.");
        }

        var existing = await userRoleRepository.GetUnrevokedAssignmentAsync(
            employeeId,
            role.Id,
            cancellationToken);

        if (existing is not null &&
            (!existing.ExpiresAtUtc.HasValue || existing.ExpiresAtUtc.Value > now))
        {
            throw new ConflictException(
                $"Employee already has the active role '{role.Code}'.");
        }

        string? beforeJson = null;
        string auditAction = "USER_ROLE_GRANTED";
        UserRole assignment;

        if (existing is not null)
        {
            // The filtered unique index allows one unrevoked row per employee/role.
            // Reuse an expired row and preserve the previous state in the audit log.
            beforeJson = SerializeAssignment(existing);
            existing.GrantedByEmployeeId = actorId;
            existing.GrantedAtUtc = now;
            existing.ExpiresAtUtc = expiresAtUtc;
            existing.RevokedByEmployeeId = null;
            existing.RevokedAtUtc = null;
            assignment = existing;
            auditAction = "USER_ROLE_RENEWED";
        }
        else
        {
            assignment = new UserRole
            {
                EmployeeId = employeeId,
                RoleId = role.Id,
                GrantedByEmployeeId = actorId,
                GrantedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            };
            userRoleRepository.Add(assignment);
        }

        userRoleRepository.Add(CreateAuditLog(
            actorId,
            employeeId,
            auditAction,
            $"{employeeId:N}:{role.Code}",
            beforeJson,
            SerializeAssignment(role, assignment),
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(role, assignment);
    }

    public async Task RevokeAsync(
        Guid actorId,
        Guid employeeId,
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        await RequireRoleManagementAsync(actorId, cancellationToken);
        EnsureNotSelf(actorId, employeeId);

        var employee = await RequireEmployeeAsync(employeeId, cancellationToken);
        var normalizedRoleCode = roleCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRoleCode))
        {
            throw ValidationError("roleCode", "Role code is required.");
        }

        var role = await userRoleRepository.GetRoleByCodeAsync(
            normalizedRoleCode,
            cancellationToken);
        if (role is null)
        {
            throw new NotFoundException($"Role '{normalizedRoleCode}' was not found.");
        }

        var assignment = await userRoleRepository.GetUnrevokedAssignmentAsync(
            employeeId,
            role.Id,
            cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException(
                $"Employee does not have an unrevoked assignment for role '{role.Code}'.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var isCurrentlyEffective =
            employee.IsActive &&
            role.IsActive &&
            (!assignment.ExpiresAtUtc.HasValue || assignment.ExpiresAtUtc.Value > now);

        if (isCurrentlyEffective)
        {
            var roleGrants = await userRoleRepository.GetRolePermissionScopesAsync(
                role.Id,
                cancellationToken);
            var grantsRoleManagement = roleGrants.Any(grant =>
                string.Equals(grant.PermissionCode, "roles.manage", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(grant.Scope, "All", StringComparison.OrdinalIgnoreCase));

            if (grantsRoleManagement &&
                !await userRoleRepository.HasOtherActiveRoleManagersAsync(
                    employeeId,
                    role.Id,
                    now,
                    cancellationToken))
            {
                throw new ConflictException(
                    "Cannot revoke the last active role-management administrator.");
            }
        }

        var beforeJson = SerializeAssignment(assignment);
        assignment.RevokedByEmployeeId = actorId;
        assignment.RevokedAtUtc = now;

        userRoleRepository.Add(CreateAuditLog(
            actorId,
            employeeId,
            "USER_ROLE_REVOKED",
            $"{employeeId:N}:{role.Code}",
            beforeJson,
            SerializeAssignment(assignment),
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<EffectiveAuthorization> RequireRoleManagementAsync(
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty)
        {
            throw new ForbiddenException();
        }

        // Re-read from HR_DB for every management operation. A stale JWT claim
        // must not be enough to grant or revoke roles after the actor loses access.
        var authorization = await authorizationService.GetEffectiveAuthorizationAsync(
            actorId,
            cancellationToken);

        if (!authorization.HasScope("roles.manage", "All"))
        {
            throw new ForbiddenException();
        }

        return authorization;
    }

    private async Task<Employee> RequireEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (employeeId == Guid.Empty)
        {
            throw new NotFoundException("Employee was not found.");
        }

        return await userRoleRepository.GetEmployeeByIdAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException($"Employee with id '{employeeId}' was not found.");
    }

    private static void EnsureNotSelf(Guid actorId, Guid targetEmployeeId)
    {
        if (actorId == targetEmployeeId)
        {
            throw new ForbiddenException("You cannot grant or revoke roles for your own account.");
        }
    }

    private static void EnsureGrantDoesNotEscalate(
        EffectiveAuthorization actorAuthorization,
        IReadOnlyList<PermissionScopeGrant> targetGrants)
    {
        foreach (var grant in targetGrants)
        {
            var actorScopes = actorAuthorization.GetScopes(grant.PermissionCode);
            var canGrantScope =
                actorScopes.Contains(grant.Scope, StringComparer.OrdinalIgnoreCase) ||
                actorScopes.Contains("All", StringComparer.OrdinalIgnoreCase);

            if (!canGrantScope)
            {
                throw new ForbiddenException(
                    $"You cannot grant role permissions beyond your own access ({grant.PermissionCode}:{grant.Scope}).");
            }
        }
    }

    private static UserRoleAssignmentResponse ToResponse(UserRole assignment) =>
        ToResponse(assignment.Role, assignment);

    private static UserRoleAssignmentResponse ToResponse(
        SecurityRole role,
        UserRole assignment) =>
        new(
            role.Code,
            role.Name,
            assignment.GrantedAtUtc,
            assignment.ExpiresAtUtc,
            assignment.GrantedByEmployeeId);

    private static string SerializeAssignment(UserRole assignment) =>
        JsonSerializer.Serialize(new
        {
            assignment.Id,
            assignment.EmployeeId,
            RoleCode = assignment.Role.Code,
            assignment.GrantedByEmployeeId,
            assignment.GrantedAtUtc,
            assignment.ExpiresAtUtc,
            assignment.RevokedByEmployeeId,
            assignment.RevokedAtUtc
        });

    private static string SerializeAssignment(SecurityRole role, UserRole assignment) =>
        JsonSerializer.Serialize(new
        {
            assignment.Id,
            assignment.EmployeeId,
            RoleCode = role.Code,
            assignment.GrantedByEmployeeId,
            assignment.GrantedAtUtc,
            assignment.ExpiresAtUtc,
            assignment.RevokedByEmployeeId,
            assignment.RevokedAtUtc
        });

    private static AuthzAuditLog CreateAuditLog(
        Guid actorId,
        Guid targetEmployeeId,
        string action,
        string targetId,
        string? beforeJson,
        string? afterJson,
        DateTime createdAtUtc) =>
        new()
        {
            ActorEmployeeId = actorId,
            TargetEmployeeId = targetEmployeeId,
            Action = action,
            TargetType = "UserRole",
            TargetId = targetId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            CreatedAtUtc = createdAtUtc
        };

    private static RequestValidationException ValidationError(string field, string message) =>
        new(new Dictionary<string, string[]>
        {
            [field] = [message]
        });
}
