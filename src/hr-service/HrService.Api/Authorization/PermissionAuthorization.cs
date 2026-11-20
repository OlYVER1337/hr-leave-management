using HrService.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HrService.Api.Authorization;

public static class AuthorizationClaimReader
{
    public static Guid? GetEmployeeId(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var employeeId) && employeeId != Guid.Empty
            ? employeeId
            : null;
    }

    public static EffectiveAuthorization Read(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.FindFirst("authz_v")?.Value != "1")
            return EffectiveAuthorization.Empty;

        var claimValue = principal.FindFirst("perm")?.Value;
        if (string.IsNullOrWhiteSpace(claimValue))
            return EffectiveAuthorization.Empty;

        try
        {
            var permissions = JsonSerializer.Deserialize<PermissionClaim[]>(claimValue);
            if (permissions is null)
                return EffectiveAuthorization.Empty;

            return new EffectiveAuthorization(permissions
                .Where(permission => permission is not null)
                .Select(permission => new EffectivePermission(
                    permission.PermissionCode ?? string.Empty,
                    permission.Scopes ?? Array.Empty<string>())));
        }
        catch (JsonException)
        {
            // Invalid or incompatible authorization claims must never grant access.
            return EffectiveAuthorization.Empty;
        }
    }

    private sealed class PermissionClaim
    {
        public PermissionClaim()
        {
        }

        [JsonPropertyName("p")]
        public string? PermissionCode { get; set; }

        [JsonPropertyName("s")]
        public string[]? Scopes { get; set; }
    }
}

public static class PermissionPolicies
{
    public const string EmployeesRead = "permission:employees.read";
    public const string EmployeesReadTeam = "permission:employees.read:team";
    public const string RolesManage = "permission:roles.manage";
    public const string EmployeesReadTeamOrAll = "permission:employees.read:team-or-all";
    public const string EmployeeCreate = "permission:employees.create";
    public const string EmployeeUpdate = "permission:employees.update";
    public const string EmployeeDeactivate = "permission:employees.deactivate";
    public const string EmployeePasswordReset = "permission:employees.password.reset";
    public const string DepartmentCreate = "permission:departments.create";
    public const string DepartmentUpdate = "permission:departments.update";
    public const string DepartmentDelete = "permission:departments.delete";
    public const string HolidayCreate = "permission:holidays.create";
    public const string HolidayUpdate = "permission:holidays.update";
    public const string HolidayDelete = "permission:holidays.delete";
    public const string LeavePolicyCreate = "permission:leave-policies.create";
    public const string LeavePolicyUpdate = "permission:leave-policies.update";
    public const string LeaveTypeCreate = "permission:leave-types.create";
    public const string LeaveTypeUpdate = "permission:leave-types.update";
}

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string PermissionCode { get; }
    public IReadOnlyList<string> AllowedScopes { get; }

    public PermissionRequirement(string permissionCode, params string[] allowedScopes)
    {
        PermissionCode = permissionCode;
        AllowedScopes = allowedScopes;
    }
}

public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var authorization = AuthorizationClaimReader.Read(context.User);
        var hasAllowedScope = authorization
            .GetScopes(requirement.PermissionCode)
            .Any(scope => requirement.AllowedScopes.Contains(
                scope,
                StringComparer.OrdinalIgnoreCase));

        if (hasAllowedScope)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(
        this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PermissionPolicies.EmployeesRead, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("employees.read", "Self", "Team", "All")));
            options.AddPolicy(PermissionPolicies.EmployeesReadTeam, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("employees.read", "Team")));
            options.AddPolicy(PermissionPolicies.RolesManage, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("roles.manage", "All")));
            options.AddPolicy(PermissionPolicies.EmployeesReadTeamOrAll, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("employees.read", "Team", "All")));
            options.AddPolicy(PermissionPolicies.EmployeeCreate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("employees.create", "All")));
            options.AddPolicy(PermissionPolicies.EmployeeUpdate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("employees.update", "All")));
            options.AddPolicy(PermissionPolicies.EmployeeDeactivate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("employees.deactivate", "All")));
            options.AddPolicy(PermissionPolicies.EmployeePasswordReset, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("employees.password.reset", "All")));
            options.AddPolicy(PermissionPolicies.DepartmentCreate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("departments.create", "All")));
            options.AddPolicy(PermissionPolicies.DepartmentUpdate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("departments.update", "All")));
            options.AddPolicy(PermissionPolicies.DepartmentDelete, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("departments.delete", "All")));
            options.AddPolicy(PermissionPolicies.HolidayCreate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("holidays.create", "All")));
            options.AddPolicy(PermissionPolicies.HolidayUpdate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("holidays.update", "All")));
            options.AddPolicy(PermissionPolicies.HolidayDelete, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("holidays.delete", "All")));
            options.AddPolicy(PermissionPolicies.LeavePolicyCreate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("leave-policies.create", "All")));
            options.AddPolicy(PermissionPolicies.LeavePolicyUpdate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("leave-policies.update", "All")));
            options.AddPolicy(PermissionPolicies.LeaveTypeCreate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("leave-types.create", "All")));
            options.AddPolicy(PermissionPolicies.LeaveTypeUpdate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement("leave-types.update", "All")));
        });
        return services;
    }
}
