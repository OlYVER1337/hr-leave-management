using HrService.Application.Abstractions;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HrService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HrDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }  
}