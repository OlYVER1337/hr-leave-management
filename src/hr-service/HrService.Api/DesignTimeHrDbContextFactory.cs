using HrService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HrService.Api;

/// <summary>
/// Tạo HrDbContext cho các lệnh EF Core ở design time.
/// Không khởi động API nên không yêu cầu cấu hình Jwt hoặc Redis.
/// </summary>
public sealed class DesignTimeHrDbContextFactory
    : IDesignTimeDbContextFactory<HrDbContext>
{
    public HrDbContext CreateDbContext(string[] args)
    {
        var apiProjectDirectory = FindApiProjectDirectory();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Development;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<DesignTimeHrDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionStringArgument = args
            .FirstOrDefault(arg => arg.StartsWith(
                "--connection=",
                StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

        var connectionString = connectionStringArgument
            ?? configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Không tìm thấy ConnectionStrings:Default. " +
                "Hãy cấu hình trong User Secrets hoặc biến môi trường " +
                "ConnectionStrings__Default trước khi chạy lệnh EF Core.");
        }

        var options = new DbContextOptionsBuilder<HrDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServer => sqlServer.EnableRetryOnFailure())
            .Options;

        return new HrDbContext(options);
    }

    private static string FindApiProjectDirectory()
    {
        var searchRoots = new[]
        {
            new DirectoryInfo(AppContext.BaseDirectory),
            new DirectoryInfo(Directory.GetCurrentDirectory())
        };

        foreach (var root in searchRoots)
        {
            for (var directory = root; directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HrService.Api.csproj")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Không tìm thấy HrService.Api.csproj để nạp cấu hình design time.");
    }
}
