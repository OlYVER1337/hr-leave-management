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
        services.AddDbContext<HrDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHashVerifier, PasswordHashVerifier>();
        services.AddScoped<InitialHrSeeder>();

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

        return services;
    }
}