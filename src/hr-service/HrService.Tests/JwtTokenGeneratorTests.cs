using HrService.Application.Dtos;
using HrService.Domain.Entities;
using HrService.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace HrService.Tests;

public sealed class JwtTokenGeneratorTests
{
    [Fact]
    public void GenerateToken_WithAuthorization_WritesVersionedStructuredPermissions()
    {
        var employee = new Employee
        {
            FullName = "Test Employee",
            Email = "employee@example.com",
            Role = Role.Manager
        };
        var authorization = new EffectiveAuthorization(
        [
            new EffectivePermission("leaves.read", ["Self", "Team"]),
            new EffectivePermission("employees.read", ["Team"])
        ]);

        var generator = CreateGenerator();

        var response = generator.GenerateToken(employee, authorization);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        Assert.Equal("1", jwt.Claims.Single(claim => claim.Type == "authz_v").Value);
        Assert.Equal(Role.Manager.ToString(), jwt.Claims.Single(claim =>
            claim.Type == System.Security.Claims.ClaimTypes.Role).Value);

        var permissionClaim = jwt.Claims.Single(claim => claim.Type == "perm").Value;
        Assert.Contains("\"p\":\"employees.read\"", permissionClaim, StringComparison.Ordinal);
        Assert.Contains("\"s\":[\"Team\"]", permissionClaim, StringComparison.Ordinal);
        Assert.Contains("\"p\":\"leaves.read\"", permissionClaim, StringComparison.Ordinal);
        Assert.Contains("\"s\":[\"Self\",\"Team\"]", permissionClaim, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateToken_WithoutAuthorization_DoesNotWritePermissionClaim()
    {
        var employee = new Employee();
        var generator = CreateGenerator();

        var response = generator.GenerateToken(employee, EffectiveAuthorization.Empty);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        Assert.Equal("1", jwt.Claims.Single(claim => claim.Type == "authz_v").Value);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "perm");
    }

    private static JwtTokenGenerator CreateGenerator()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "01234567890123456789012345678901",
            AccessTokenMinutes = 15
        });

        return new JwtTokenGenerator(options, TimeProvider.System);
    }
}
