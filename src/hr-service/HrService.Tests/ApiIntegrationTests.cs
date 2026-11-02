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

    [Fact]
    public async Task Login_SetsHttpOnlyRefreshCookieAndReturnsOnlyAccessToken()
    {
        var employee = new HrService.Domain.Entities.Employee
        {
            Email = "employee@example.com",
            FullName = "Test Employee",
            Role = HrService.Domain.Entities.Role.Employee
        };
        var employeeRepository = new Mock<IEmployeeRepository>();
        employeeRepository
            .Setup(repository => repository.GetActiveByEmailAsync(
                employee.Email,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(hasher => hasher.Verify(employee, "password")).Returns(true);
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var tokenGenerator = new Mock<IJwtTokenGenerator>();
        tokenGenerator
            .Setup(generator => generator.GenerateToken(employee))
            .Returns(new HrService.Application.Dtos.AuthenticationResponse(
                "access-token",
                DateTimeOffset.UtcNow.AddMinutes(15),
                employee.FullName,
                employee.Email,
                employee.Id,
                employee.Role.ToString()));

        using var host = await CreateHostAsync(
            employeeRepository,
            passwordHasher,
            refreshTokenRepository,
            unitOfWork,
            tokenGenerator);
        using var client = host.GetTestClient();
        using var content = new StringContent(
            """{"email":"employee@example.com","password":"password"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("accessToken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IHost> CreateHostAsync(
        Mock<IEmployeeRepository>? employeeRepository = null,
        Mock<IPasswordHasher>? passwordHasher = null,
        Mock<IRefreshTokenRepository>? refreshTokenRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IJwtTokenGenerator>? tokenGenerator = null)
    {
        employeeRepository ??= new Mock<IEmployeeRepository>();
        var departmentRepository = new Mock<IDepartmentRepository>();
        var leaveCatalogRepository = new Mock<ILeaveCatalogRepository>();
        var cache = new Mock<ILeaveCatalogCache>();
        unitOfWork ??= new Mock<IUnitOfWork>();
        passwordHasher ??= new Mock<IPasswordHasher>();
        refreshTokenRepository ??= new Mock<IRefreshTokenRepository>();
        tokenGenerator ??= new Mock<IJwtTokenGenerator>();

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
                    services.AddSingleton<IRefreshTokenRepository>(refreshTokenRepository.Object);
                    services.AddSingleton<IJwtTokenGenerator>(tokenGenerator.Object);
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
