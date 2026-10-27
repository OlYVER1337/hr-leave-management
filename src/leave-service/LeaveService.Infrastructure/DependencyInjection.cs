using LeaveService.Application.Abstractions;
using LeaveService.Infrastructure.Clients;
using LeaveService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLeaveInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
        var hrServiceUrl = configuration["Services:Hr"]
            ?? throw new InvalidOperationException("Configuration 'Services:Hr' is required.");

        services.AddDbContext<LeaveDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
        services.AddHttpClient<ILeaveHrClient, HrServiceClient>(client =>
        {
            client.BaseAddress = new Uri(hrServiceUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
