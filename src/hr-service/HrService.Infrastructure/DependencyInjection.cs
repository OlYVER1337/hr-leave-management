using HrService.Application.Abstractions;
using HrService.Infrastructure.Authentication;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HrService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HrDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IPasswordHasher, Authentication.PasswordHasher>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<InitialHrSeeder>();
        services.AddScoped<DemoDataSeeder>();
        services.AddSingleton<DatabaseInitializer>();

        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddSingleton<IValidateOptions<InitialHrOptions>, InitialHrOptionsValidator>();
        services.AddSingleton<IValidateOptions<DemoDataOptions>, DemoDataOptionsValidator>();

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateOnStart();

        services.AddOptions<InitialHrOptions>()
            .BindConfiguration(InitialHrOptions.SectionName)
            .ValidateOnStart();

        services.AddOptions<DemoDataOptions>()
            .BindConfiguration(DemoDataOptions.SectionName)
            .ValidateOnStart();

        return services;
    }
}