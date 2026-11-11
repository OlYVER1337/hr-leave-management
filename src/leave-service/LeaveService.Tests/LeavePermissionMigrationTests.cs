using LeaveService.Api.Controllers;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace LeaveService.Tests;

public sealed class LeavePermissionMigrationTests
{
    [Theory]
    [InlineData(nameof(LeaveRequestsController.GetVisible), "permission:leaves.read")]
    [InlineData(nameof(LeaveRequestsController.GetCalendar), "permission:leaves.read")]
    [InlineData(nameof(LeaveRequestsController.GetById), "permission:leaves.read")]
    [InlineData(nameof(LeaveRequestsController.GetMine), "permission:leaves.read")]
    [InlineData(nameof(LeaveRequestsController.Create), "permission:leaves.create")]
    [InlineData(nameof(LeaveRequestsController.GetPendingApproval), "permission:leaves.decide")]
    [InlineData(nameof(LeaveRequestsController.Approve), "permission:leaves.decide")]
    [InlineData(nameof(LeaveRequestsController.Reject), "permission:leaves.decide")]
    [InlineData(nameof(LeaveRequestsController.Cancel), "permission:leaves.cancel")]
    public void LeaveEndpoint_UsesPermissionPolicyWithoutLegacyRole(
        string actionName,
        string expectedPolicy)
    {
        var method = typeof(LeaveRequestsController).GetMethod(actionName);
        Assert.NotNull(method);
        var authorization = method!.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();
        Assert.Contains(authorization, attribute =>
            attribute.Policy == expectedPolicy && attribute.Roles is null);
        Assert.DoesNotContain(authorization, attribute => !string.IsNullOrWhiteSpace(attribute.Roles));
    }

    [Fact]
    public void LeaveRequestService_DoesNotAcceptIsHrFlag()
    {
        AssertNoBooleanParameters(nameof(LeaveRequestService.GetVisibleAsync));
        AssertNoBooleanParameters(nameof(LeaveRequestService.GetPendingAsync));
        AssertNoBooleanParameters(nameof(LeaveRequestService.DecideAsync));
    }

    [Fact]
    public void LeaveRequestService_UsesLeaveAccessFromApplicationPermissions()
    {
        var accessType = typeof(LeaveRequestService).Assembly.GetType(
            "LeaveService.Application.Dtos.LeaveAccess");
        Assert.NotNull(accessType);

        Assert.Contains(accessType!.GetProperties(), property =>
            property.Name == "CanReadAll" && property.PropertyType == typeof(bool));
        Assert.Contains(accessType.GetProperties(), property =>
            property.Name == "CanDecideAll" && property.PropertyType == typeof(bool));
        Assert.Contains(accessType.GetProperties(), property =>
            property.Name == "CanDecideTeam" && property.PropertyType == typeof(bool));
    }

    [Fact]
    public void LeaveAccess_ReadsPermissionAndScopeFromVersionedJwtClaims()
    {
        var claims = new[]
        {
            new Claim("authz_v", "1"),
            new Claim("perm", """[{"p":"leaves.read","s":["Self","All"]},{"p":"leaves.decide","s":["Team"]}]""")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var access = LeaveAccess.FromClaims(principal);

        Assert.True(access.CanReadSelf);
        Assert.True(access.CanReadAll);
        Assert.True(access.CanDecideTeam);
        Assert.False(access.CanDecideAll);
    }

    [Fact]
    public void LeaveAccess_RejectsInvalidOrUnversionedPermissionClaims()
    {
        var invalidPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("authz_v", "1"),
            new Claim("perm", "not-json")
        ], "test"));
        var oldTokenPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("perm", """[{"p":"leaves.decide","s":["All"]}]""")
        ], "test"));

        Assert.False(LeaveAccess.FromClaims(invalidPrincipal).CanDecide);
        Assert.False(LeaveAccess.FromClaims(oldTokenPrincipal).CanDecide);
    }

    private static void AssertNoBooleanParameters(string methodName)
    {
        var method = typeof(LeaveRequestService).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.DoesNotContain(method!.GetParameters(), parameter => parameter.Name == "isHr");
    }
}
