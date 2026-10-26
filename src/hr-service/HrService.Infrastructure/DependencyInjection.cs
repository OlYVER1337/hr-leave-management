using HrService.Application.Abstractions;
using HrService.Infrastructure.Authentication;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace HrService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HrDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHashGenerator, PasswordHashGenerator>();
        services.AddScoped<IPasswordHashVerifier, PasswordHashVerifier>();
        services.AddScoped<InitialHrSeeder>();
        services.AddScoped<DemoDataSeeder>();

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 32 bytes.")
            .Validate(options => options.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes must be positive.")
            .ValidateOnStart();

        services.AddOptions<InitialHrOptions>()
            .BindConfiguration(InitialHrOptions.SectionName)
            .Validate(
                options => !options.Enabled ||
                    (!string.IsNullOrWhiteSpace(options.Email) && options.Email.Length <= 200),
                "InitialHr:Email is required and must not exceed 200 characters when seeding is enabled.")
            .Validate(
                options => !options.Enabled ||
                    (!string.IsNullOrWhiteSpace(options.Password) && options.Password.Length >= 12),
                "InitialHr:Password must contain at least 12 characters when seeding is enabled.")
            .Validate(
                options => !options.Enabled ||
                    (!string.IsNullOrWhiteSpace(options.FullName) && options.FullName.Trim().Length <= 150),
                "InitialHr:FullName is required and must not exceed 150 characters when seeding is enabled.")
            .Validate(
                options => !options.Enabled ||
                    (!string.IsNullOrWhiteSpace(options.Code) && options.Code.Trim().Length <= 20),
                "InitialHr:Code is required and must not exceed 20 characters when seeding is enabled.")
            .Validate(
                options => !options.Enabled ||
                    (!string.IsNullOrWhiteSpace(options.DepartmentName) &&
                        options.DepartmentName.Trim().Length <= 100),
                "InitialHr:DepartmentName is required and must not exceed 100 characters when seeding is enabled.")
            .ValidateOnStart();

        services.AddOptions<DemoDataOptions>()
            .BindConfiguration(DemoDataOptions.SectionName)
            .Validate(
                options => !options.Enabled ||
                    (!string.IsNullOrWhiteSpace(options.DepartmentName) &&
                        options.DepartmentName.Trim().Length <= 100),
                "DemoData:DepartmentName is required and must not exceed 100 characters when seeding is enabled.")
            .Validate(
                options => !options.Enabled || options.SickDaysPerYear > 0,
                "DemoData:SickDaysPerYear must be positive when seeding is enabled.")
            .Validate(
                options => !options.Enabled ||
                    IsValidDemoAccount(options.Manager),
                "DemoData:Manager requires a valid email, a password of at least 12 characters, a full name, and a code when seeding is enabled.")
            .Validate(
                options => !options.Enabled ||
                    IsValidDemoAccount(options.Employee),
                "DemoData:Employee requires a valid email, a password of at least 12 characters, a full name, and a code when seeding is enabled.")
            .Validate(
                options => !options.Enabled ||
                    !string.Equals(options.Manager.Email.Trim(), options.Employee.Email.Trim(), StringComparison.OrdinalIgnoreCase),
                "DemoData manager and employee emails must be different.")
            .Validate(
                options => !options.Enabled ||
                    !string.Equals(options.Manager.Code.Trim(), options.Employee.Code.Trim(), StringComparison.OrdinalIgnoreCase),
                "DemoData manager and employee codes must be different.")
            .ValidateOnStart();

        return services;
    }

    private static bool IsValidDemoAccount(DemoAccountOptions account) =>
        !string.IsNullOrWhiteSpace(account.Email) &&
        account.Email.Length <= 200 &&
        !string.IsNullOrWhiteSpace(account.Password) &&
        account.Password.Length >= 12 &&
        !string.IsNullOrWhiteSpace(account.FullName) &&
        account.FullName.Trim().Length <= 150 &&
        !string.IsNullOrWhiteSpace(account.Code) &&
        account.Code.Trim().Length <= 20;
}