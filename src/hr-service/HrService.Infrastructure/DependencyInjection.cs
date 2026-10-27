using HrService.Application.Abstractions;
using HrService.Infrastructure.Authentication;
using HrService.Infrastructure.Caching;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HrService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<HrDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure()));
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ILeaveCatalogRepository, LeaveCatalogRepository>();
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
        services.AddSingleton<IValidateOptions<CacheOptions>, CacheOptionsValidator>();

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateOnStart();

        services.AddOptions<InitialHrOptions>()
            .BindConfiguration(InitialHrOptions.SectionName)
            .ValidateOnStart();

        services.AddOptions<DemoDataOptions>()
            .BindConfiguration(DemoDataOptions.SectionName)
            .ValidateOnStart();

        services.AddOptions<CacheOptions>()
            .BindConfiguration(CacheOptions.SectionName)
            .ValidateOnStart();

        var cacheOptions = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()
            ?? new CacheOptions();
        if (cacheOptions.Enabled)
        {
            var redisConnectionString = configuration["Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException(
                    "Redis:ConnectionString is required when Cache:Enabled is true.");
            }

            services.AddStackExchangeRedisCache(options =>
            {
                var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
                redisOptions.AbortOnConnectFail = false;
                redisOptions.ConnectRetry = 1;
                redisOptions.ConnectTimeout = 1000;
                redisOptions.AsyncTimeout = 1000;
                redisOptions.SyncTimeout = 1000;
                options.ConfigurationOptions = redisOptions;
            });
            services.AddSingleton<ILeaveCatalogCache, RedisLeaveCatalogCache>();
        }
        else
        {
            services.AddSingleton<ILeaveCatalogCache, DisabledLeaveCatalogCache>();
        }

        return services;
    }
}