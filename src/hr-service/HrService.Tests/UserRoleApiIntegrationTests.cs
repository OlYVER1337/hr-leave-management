using HrService.Api.Authorization;
using HrService.Api.Controllers;
using HrService.Api.Middleware;
using HrService.Application.Abstractions;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace HrService.Tests;

public sealed class UserRoleApiIntegrationTests
{
    [Fact]
    public async Task RolesEndpoint_RejectsLegacyHrRoleWithoutPermissionClaim()
    {
        using var host = await CreateHostAsync(
            new Mock<IUserRoleRepository>(),
            new Mock<IAuthorizationRepository>());
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "HR");

        var response = await client.GetAsync("/api/v1/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RolesEndpoint_RechecksCurrentDatabasePermissionAfterJwtPolicyPasses()
    {
        var actorId = Guid.NewGuid();
        var userRoleRepository = new Mock<IUserRoleRepository>();
        var authorizationRepository = new Mock<IAuthorizationRepository>();
        authorizationRepository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                actorId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using var host = await CreateHostAsync(userRoleRepository, authorizationRepository);
        using var client = host.GetTestClient();
        AddAuthorizationHeaders(
            client,
            actorId,
            """[{"p":"roles.manage","s":["All"]}]""");

        var response = await client.GetAsync("/api/v1/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        userRoleRepository.Verify(
            item => item.ListActiveRolesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RolesEndpoint_ReturnsActiveRolesForAuthorizedCaller()
    {
        var actorId = Guid.NewGuid();
        var userRoleRepository = new Mock<IUserRoleRepository>();
        userRoleRepository
            .Setup(item => item.ListActiveRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SecurityRole
                {
                    Id = 4,
                    Code = "Auditor",
                    Name = "Kiểm toán",
                    Description = "Chỉ xem dữ liệu.",
                    IsSystem = true,
                    IsActive = true
                }
            ]);
        var authorizationRepository = new Mock<IAuthorizationRepository>();
        authorizationRepository
            .Setup(item => item.GetEffectivePermissionScopesAsync(
                actorId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PermissionScopeGrant("roles.manage", "All")]);
        using var host = await CreateHostAsync(userRoleRepository, authorizationRepository);
        using var client = host.GetTestClient();
        AddAuthorizationHeaders(
            client,
            actorId,
            """[{"p":"roles.manage","s":["All"]}]""");

        var response = await client.GetAsync("/api/v1/roles");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Auditor", body, StringComparison.Ordinal);
        Assert.Contains("Kiểm toán", body, StringComparison.Ordinal);
    }

    private static void AddAuthorizationHeaders(
        HttpClient client,
        Guid employeeId,
        string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-Employee-Id", employeeId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Authz-Version", "1");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static async Task<IHost> CreateHostAsync(
        Mock<IUserRoleRepository> userRoleRepository,
        Mock<IAuthorizationRepository> authorizationRepository)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(EmployeeRolesController).Assembly);
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                    services.AddPermissionAuthorization();
                    services.AddScoped<AuthorizationService>();
                    services.AddScoped<UserRoleService>();
                    services.AddSingleton<IUserRoleRepository>(userRoleRepository.Object);
                    services.AddSingleton<IAuthorizationRepository>(authorizationRepository.Object);
                    services.AddSingleton<IUnitOfWork>(unitOfWork.Object);
                    services.AddSingleton(TimeProvider.System);
                });
                webHost.Configure(app =>
                {
                    app.UseMiddleware<ExceptionHandlingMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        var builtHost = host.Build();
        await builtHost.StartAsync();
        return builtHost;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "UserRoleIntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            var employeeIdValue = Request.Headers["X-Test-Employee-Id"].FirstOrDefault();
            var authzVersion = Request.Headers["X-Test-Authz-Version"].FirstOrDefault();
            var permissions = Request.Headers["X-Test-Permissions"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(role) &&
                string.IsNullOrWhiteSpace(employeeIdValue) &&
                string.IsNullOrWhiteSpace(authzVersion) &&
                string.IsNullOrWhiteSpace(permissions))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var employeeId = Guid.TryParse(employeeIdValue, out var parsedEmployeeId)
                ? parsedEmployeeId
                : Guid.NewGuid();
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, employeeId.ToString())
            };

            if (!string.IsNullOrWhiteSpace(role))
                claims.Add(new Claim(ClaimTypes.Role, role));
            if (!string.IsNullOrWhiteSpace(authzVersion))
                claims.Add(new Claim("authz_v", authzVersion));
            if (!string.IsNullOrWhiteSpace(permissions))
                claims.Add(new Claim("perm", permissions));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
