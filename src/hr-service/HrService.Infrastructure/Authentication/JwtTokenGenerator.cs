using HrService.Application.Abstractions;
using HrService.Application.Dtos;
using HrService.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace HrService.Infrastructure.Authentication;

public sealed class JwtTokenGenerator(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IJwtTokenGenerator
{
    private readonly JwtOptions _jwtOptions = options.Value;

    public AuthenticationResponse GenerateToken(
        Employee employee,
        EffectiveAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(authorization);

        var now = timeProvider.GetUtcNow();
        var expiresAtUtc = now.AddMinutes(_jwtOptions.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, employee.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, employee.FullName),
            new(JwtRegisteredClaimNames.Email, employee.Email),
            // Temporary compatibility claim for UI only. Authorization decisions
            // must use authz_v + perm, not the legacy role claim.
            new(ClaimTypes.Role, employee.Role.ToString()),
            new("authz_v", "1"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (authorization.Permissions.Count > 0)
        {
            var permissions = authorization.Permissions
                .Select(permission => new JwtPermission(
                    permission.Code,
                    permission.Scopes))
                .ToArray();

            claims.Add(new Claim(
                "perm",
                JsonSerializer.Serialize(permissions)));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials
        );

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new AuthenticationResponse(
            AccessToken: accessToken,
            ExpiresAtUtc: expiresAtUtc,
            FullName: employee.FullName,
            Email: employee.Email,
            UserId: employee.Id,
            Role: employee.Role.ToString()
        );
    }

    private sealed record JwtPermission(string p, IReadOnlyList<string> s);
}
