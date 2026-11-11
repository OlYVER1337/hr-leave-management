namespace HrService.Application.Dtos;

public sealed record PermissionScopeGrant(
    string PermissionCode,
    string Scope);

public sealed record EffectivePermission(
    string Code,
    IReadOnlyList<string> Scopes);

public sealed class EffectiveAuthorization
{
    private readonly IReadOnlyDictionary<string, EffectivePermission> _permissionsByCode;

    public IReadOnlyList<EffectivePermission> Permissions { get; }

    public static EffectiveAuthorization Empty { get; } =
        new(Array.Empty<EffectivePermission>());

    public EffectiveAuthorization(IEnumerable<EffectivePermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var mergedPermissions = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission.Code))
            .GroupBy(
                permission => permission.Code.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var scopes = group
                    .SelectMany(permission => permission.Scopes ?? Array.Empty<string>())
                    .Where(scope => !string.IsNullOrWhiteSpace(scope))
                    .Select(scope => scope.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new EffectivePermission(group.First().Code.Trim(), scopes);
            })
            .Where(permission => permission.Scopes.Count > 0)
            .OrderBy(permission => permission.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Permissions = Array.AsReadOnly(mergedPermissions);
        _permissionsByCode = mergedPermissions.ToDictionary(
            permission => permission.Code,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool HasPermission(string? permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode) &&
        _permissionsByCode.ContainsKey(permissionCode.Trim());

    public IReadOnlyList<string> GetScopes(string? permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return Array.Empty<string>();
        }

        return _permissionsByCode.TryGetValue(permissionCode.Trim(), out var permission)
            ? permission.Scopes
            : Array.Empty<string>();
    }

    public bool HasScope(string? permissionCode, string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }

        return GetScopes(permissionCode).Contains(
            scope.Trim(),
            StringComparer.OrdinalIgnoreCase);
    }
}
