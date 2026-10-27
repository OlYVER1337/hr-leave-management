using HrService.Infrastructure.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrService.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer> logger)
{
    private const int MaxAttempts = 5;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var services = scope.ServiceProvider;
                var dbContext = services.GetRequiredService<HrDbContext>();

                await dbContext.Database.MigrateAsync(cancellationToken);
                await services.GetRequiredService<InitialHrSeeder>().SeedAsync(cancellationToken);
                await services.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (
                attempt < MaxAttempts && IsTransientSqlException(exception))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(
                    exception,
                    "Database initialization attempt {Attempt}/{MaxAttempts} failed due to a transient SQL Server error. Retrying in {Delay}.",
                    attempt,
                    MaxAttempts,
                    delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransientSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.IsTransient)
            {
                return true;
            }
        }

        return false;
    }
}
