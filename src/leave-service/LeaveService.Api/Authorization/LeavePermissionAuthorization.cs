using LeaveService.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveService.Api.Authorization;

public static class LeavePermissionPolicies
{
    public const string LeavesRead = "permission:leaves.read";
    public const string LeavesCreate = "permission:leaves.create";
    public const string LeavesDecide = "permission:leaves.decide";
    public const string LeavesCancel = "permission:leaves.cancel";
}

public sealed class LeavePermissionRequirement : IAuthorizationRequirement
{
    public string PermissionCode { get; }
    public IReadOnlyList<string> AllowedScopes { get; }

    public LeavePermissionRequirement(string permissionCode, params string[] allowedScopes)
    {
        PermissionCode = permissionCode;
        AllowedScopes = allowedScopes;
    }
}

public sealed class LeavePermissionAuthorizationHandler
    : AuthorizationHandler<LeavePermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        LeavePermissionRequirement requirement)
    {
        var access = LeaveAccess.FromClaims(context.User);
        if (requirement.AllowedScopes.Any(scope => access.HasScope(requirement.PermissionCode, scope)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class LeavePermissionAuthorizationExtensions
{
    public static IServiceCollection AddLeavePermissionAuthorization(
        this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, LeavePermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(LeavePermissionPolicies.LeavesRead, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new LeavePermissionRequirement(
                        "leaves.read", "Self", "Team", "All")));
            options.AddPolicy(LeavePermissionPolicies.LeavesCreate, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new LeavePermissionRequirement("leaves.create", "Self")));
            options.AddPolicy(LeavePermissionPolicies.LeavesDecide, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new LeavePermissionRequirement("leaves.decide", "Team", "All")));
            options.AddPolicy(LeavePermissionPolicies.LeavesCancel, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new LeavePermissionRequirement("leaves.cancel", "Self")));
        });
        return services;
    }
}
