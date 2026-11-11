using HrService.Application.Abstractions;
using HrService.Application.Dtos;

namespace HrService.Application.Services;

public sealed class AuthorizationService(
    IAuthorizationRepository authorizationRepository,
    TimeProvider timeProvider)
{
    private static readonly IReadOnlyDictionary<string, string> SupportedScopes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Self"] = "Self",
            ["Team"] = "Team",
            ["All"] = "All"
        };

    public async Task<EffectiveAuthorization> GetEffectiveAuthorizationAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("Employee ID is required.", nameof(employeeId));
        }

        var grants = await authorizationRepository.GetEffectivePermissionScopesAsync(
            employeeId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        // Department is intentionally excluded in V1 until each resource has
        // an implemented and tested Department scope resolver.
        var supportedGrants = grants
            .Where(grant =>
                !string.IsNullOrWhiteSpace(grant.PermissionCode) &&
                !string.IsNullOrWhiteSpace(grant.Scope) &&
                SupportedScopes.ContainsKey(grant.Scope))
            .Select(grant => new PermissionScopeGrant(
                grant.PermissionCode.Trim(),
                SupportedScopes[grant.Scope.Trim()]));

        var permissions = supportedGrants
            .GroupBy(grant => grant.PermissionCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EffectivePermission(
                group.First().PermissionCode,
                group.Select(grant => grant.Scope)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()));

        return new EffectiveAuthorization(permissions);
    }
}
