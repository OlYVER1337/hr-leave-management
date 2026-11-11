using HrService.Application.Dtos;

namespace HrService.Application.Abstractions;

public interface IAuthorizationRepository
{
    Task<IReadOnlyList<PermissionScopeGrant>> GetEffectivePermissionScopesAsync(
        Guid employeeId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
