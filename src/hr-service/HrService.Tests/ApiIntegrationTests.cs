using HrService.Api.Controllers;
using HrService.Api.Middleware;
using HrService.Application.Abstractions;
using HrService.Application.Services;
using Microsoft.AspNetCore.Builder;
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
using System.Text;
using Xunit;

namespace HrService.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task EmployeeList_RequiresAuthentication()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/v1/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EmployeeList_RequiresHrRole()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Employee");

        var response = await client.GetAsync("/api/v1/employees");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EmployeeList_ReturnsOkForHrRole()
    {
        var employeeRepository = new Mock<IEmployeeRepository>();
        employeeRepository
            .Setup(repository => repository.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using var host = await CreateHostAsync(employeeRepository);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "HR");

        var response = await client.GetAsync("/api/v1/employees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsProblemDetailsUnauthorized()
    {
        var employeeRepository = new Mock<IEmployeeRepository>();
        employeeRepository
            .Setup(repository => repository.GetActiveByEmailAsync(
                "unknown@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((HrService.Domain.Entities.Employee?)null);
        using var host = await CreateHostAsync(employeeRepository);
        using var client = host.GetTestClient();
        using var content = new StringContent(
            """{"email":"unknown@example.com","password":"some-password"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            response.Content.Headers.ContentType?.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IHost> CreateHostAsync(
        Mock<IEmployeeRepository>? employeeRepository = null)
    {
        employeeRepository ??= new Mock<IEmployeeRepository>();
        var departmentRepository = new Mock<IDepartmentRepository>();
        var leaveCatalogRepository = new Mock<ILeaveCatalogRepository>();
        var cache = new Mock<ILeaveCatalogCache>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var passwordHasher = new Mock<IPasswordHasher>();
        var tokenGenerator = new Mock<IJwtTokenGenerator>();

        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(EmployeesController).Assembly);
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                    services.AddAuthorization();
                    services.AddScoped<DepartmentService>();
                    services.AddScoped<EmployeeService>();
                    services.AddScoped<LeaveCatalogService>();
                    services.AddScoped<HrService.Application.Services.AuthenticationService>();
                    services.AddSingleton<IEmployeeRepository>(employeeRepository.Object);
                    services.AddSingleton<IDepartmentRepository>(departmentRepository.Object);
                    services.AddSingleton<ILeaveCatalogRepository>(leaveCatalogRepository.Object);
                    services.AddSingleton<ILeaveCatalogCache>(cache.Object);
                    services.AddSingleton<IUnitOfWork>(unitOfWork.Object);
                    services.AddSingleton<IPasswordHasher>(passwordHasher.Object);
                    services.AddSingleton<IJwtTokenGenerator>(tokenGenerator.Object);
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
        public const string SchemeName = "IntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
