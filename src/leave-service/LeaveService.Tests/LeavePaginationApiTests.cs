using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LeaveService.Api.Authorization;
using LeaveService.Api.Controllers;
using LeaveService.Application.Abstractions;
using LeaveService.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeaveService.Tests;

public sealed class LeavePaginationApiTests
{
    private const string Permissions =
        """[{"p":"leaves.read","s":["All"]},{"p":"leaves.decide","s":["All"]}]""";

    [Theory]
    [InlineData("/api/v1/leave-requests", "")]
    [InlineData("/api/v1/leave-requests", "?page=1")]
    [InlineData("/api/v1/leave-requests", "?pageSize=10")]
    [InlineData("/api/v1/leave-requests", "?page=0&pageSize=10")]
    [InlineData("/api/v1/leave-requests", "?page=1&pageSize=101")]
    [InlineData("/api/v1/leave-requests/mine", "")]
    [InlineData("/api/v1/leave-requests/mine", "?page=1")]
    [InlineData("/api/v1/leave-requests/mine", "?pageSize=10")]
    [InlineData("/api/v1/leave-requests/mine", "?page=0&pageSize=10")]
    [InlineData("/api/v1/leave-requests/mine", "?page=1&pageSize=101")]
    [InlineData("/api/v1/leave-requests/pending-approval", "")]
    [InlineData("/api/v1/leave-requests/pending-approval", "?page=1")]
    [InlineData("/api/v1/leave-requests/pending-approval", "?pageSize=10")]
    [InlineData("/api/v1/leave-requests/pending-approval", "?page=0&pageSize=10")]
    [InlineData("/api/v1/leave-requests/pending-approval", "?page=1&pageSize=101")]
    public async Task ListEndpoints_RequireValidPageAndPageSize(
        string path,
        string query)
    {
        var repository = new Mock<ILeaveRequestRepository>();
        var hrClient = new Mock<ILeaveHrClient>();

        using var host = await CreateHostAsync(repository, hrClient);
        using var client = host.GetTestClient();
        AddPermissionHeaders(client, Guid.NewGuid());

        var response = await client.GetAsync($"{path}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        repository.VerifyNoOtherCalls();
        hrClient.VerifyNoOtherCalls();
    }

    private static void AddPermissionHeaders(HttpClient client, Guid employeeId)
    {
        client.DefaultRequestHeaders.Add("X-Test-Employee-Id", employeeId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Authz-Version", "1");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", Permissions);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
    }

    private static async Task<IHost> CreateHostAsync(
        Mock<ILeaveRequestRepository> repository,
        Mock<ILeaveHrClient> hrClient)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(LeaveRequestsController).Assembly);
                    services
                        .AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                            options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                            options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                            TestAuthenticationHandler.SchemeName,
                            _ => { });
                    services.AddLeavePermissionAuthorization();
                    services.AddScoped<LeaveRequestService>();
                    services.AddSingleton<ILeaveRequestRepository>(repository.Object);
                    services.AddSingleton<ILeaveHrClient>(hrClient.Object);
                    services.AddSingleton(TimeProvider.System);
                });
                webHost.Configure(app =>
                {
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
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "LeavePaginationIntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var employeeId = Request.Headers["X-Test-Employee-Id"].FirstOrDefault();
            var authzVersion = Request.Headers["X-Test-Authz-Version"].FirstOrDefault();
            var permissions = Request.Headers["X-Test-Permissions"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(employeeId) ||
                string.IsNullOrWhiteSpace(authzVersion) ||
                string.IsNullOrWhiteSpace(permissions))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, employeeId),
                new Claim("authz_v", authzVersion),
                new Claim("perm", permissions)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
