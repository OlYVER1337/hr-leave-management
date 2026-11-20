using System.Security.Claims;
using System.Text.Json;

namespace LeaveService.Application.Dtos;

public sealed record LeavePermissionGrant(
    string Code,
    IReadOnlyList<string> Scopes);

/// <summary>
/// Effective leave permissions carried by the signed JWT permission claim.
/// This object intentionally contains capabilities, not legacy role names.
/// </summary>
public sealed class LeaveAccess
{
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _scopesByPermission;

    public static LeaveAccess Empty { get; } = new([]);

    public bool CanReadSelf => HasScope("leaves.read", "Self");
    public bool CanReadTeam => HasScope("leaves.read", "Team");
    public bool CanReadAll => HasScope("leaves.read", "All");
    public bool CanCreate => HasScope("leaves.create", "Self");
    public bool CanDecideTeam => HasScope("leaves.decide", "Team");
    public bool CanDecideAll => HasScope("leaves.decide", "All");
    public bool CanCancel => HasScope("leaves.cancel", "Self");

    public bool CanRead => CanReadSelf || CanReadTeam || CanReadAll;
    public bool CanDecide => CanDecideTeam || CanDecideAll;

    public LeaveAccess(IEnumerable<LeavePermissionGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        _scopesByPermission = grants
            .Where(grant => grant is not null && !string.IsNullOrWhiteSpace(grant.Code))
            .GroupBy(grant => grant.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)new HashSet<string>(
                    group.SelectMany(grant => grant.Scopes ?? Array.Empty<string>())
                        .Where(scope => !string.IsNullOrWhiteSpace(scope))
                        .Select(scope => scope.Trim()),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool HasScope(string permissionCode, string scope)
    {
        return !string.IsNullOrWhiteSpace(permissionCode) &&
               !string.IsNullOrWhiteSpace(scope) &&
               _scopesByPermission.TryGetValue(permissionCode.Trim(), out var scopes) &&
               scopes.Contains(scope.Trim());
    }

    public static LeaveAccess FromClaims(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.FindFirst("authz_v")?.Value != "1")
        {
            return Empty;
        }

        var permissionJson = principal.FindFirst("perm")?.Value;
        if (string.IsNullOrWhiteSpace(permissionJson))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(permissionJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Empty;
            }

            var grants = new List<LeavePermissionGrant>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("p", out var permissionElement) ||
                    permissionElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var permissionCode = permissionElement.GetString();
                if (string.IsNullOrWhiteSpace(permissionCode) ||
                    !item.TryGetProperty("s", out var scopesElement) ||
                    scopesElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var scopes = scopesElement.EnumerateArray()
                    .Where(scope => scope.ValueKind == JsonValueKind.String)
                    .Select(scope => scope.GetString() ?? string.Empty)
                    .Where(scope => !string.IsNullOrWhiteSpace(scope))
                    .ToArray();

                grants.Add(new LeavePermissionGrant(permissionCode, scopes));
            }

            return new LeaveAccess(grants);
        }
        catch (JsonException)
        {
            // Malformed or incompatible permission claims fail closed.
            return Empty;
        }
    }
}
