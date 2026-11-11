using System.Security.Claims;
using HrService.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HrService.Tests;

public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task LegacyHrRoleWithoutPermissionClaim_IsDenied()
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.Role, "HR"));

        var succeeded = await AuthorizesAsync(principal, "employees.create", "All");

        Assert.False(succeeded);
    }

    [Fact]
    public async Task PermissionClaimWithoutRole_IsAllowedForMatchingScope()
    {
        var principal = CreatePrincipal(
            new Claim("authz_v", "1"),
            new Claim("perm", """[{"p":"employees.create","s":["All"]}]"""));

        var succeeded = await AuthorizesAsync(principal, "employees.create", "All");

        Assert.True(succeeded);
    }

    [Theory]
    [InlineData("0", """[{"p":"employees.create","s":["All"]}]""")]
    [InlineData("1", """[{"p":"employees.create","s":["Team"]}]""")]
    [InlineData("1", """[{"p":"employees.update","s":["All"]}]""")]
    [InlineData("1", """[{"p":"employees.create","s":["Department"]}]""")]
    public async Task PermissionAuthorization_RejectsWrongVersionPermissionOrScope(
        string version,
        string permissions)
    {
        var principal = CreatePrincipal(
            new Claim("authz_v", version),
            new Claim("perm", permissions));

        var succeeded = await AuthorizesAsync(principal, "employees.create", "All");

        Assert.False(succeeded);
    }

    private static async Task<bool> AuthorizesAsync(
        ClaimsPrincipal principal,
        string permission,
        string scope)
    {
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(permission, scope)],
            principal,
            resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
