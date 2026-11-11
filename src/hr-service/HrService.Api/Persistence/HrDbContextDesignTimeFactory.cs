using HrService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HrService.Api.Persistence;

/// <summary>
/// Tạo HrDbContext cho các lệnh EF Core ở design time.
/// Factory này tránh khởi chạy Program.cs, do đó không yêu cầu cấu hình Jwt
/// chỉ để tạo migration.
/// </summary>
public sealed class HrDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<HrDbContext>
{
    public HrDbContext CreateDbContext(string[] args)
    {
        var apiProjectDirectory = FindApiProjectDirectory();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<HrDbContextDesignTimeFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Không tìm thấy ConnectionStrings:Default. " +
                "Hãy cấu hình User Secrets hoặc biến môi trường ConnectionStrings__Default " +
                "trước khi chạy lệnh dotnet ef.");
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
        var startingDirectories = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var startingDirectory in startingDirectories)
        {
            var current = new DirectoryInfo(startingDirectory);

            while (current is not null)
            {
                var directProjectFile = Path.Combine(
                    current.FullName,
                    "HrService.Api.csproj");

                if (File.Exists(directProjectFile))
                {
                    return current.FullName;
                }

                var nestedProjectDirectory = Path.Combine(
                    current.FullName,
                    "src",
                    "hr-service",
                    "HrService.Api");

                if (File.Exists(Path.Combine(
                        nestedProjectDirectory,
                        "HrService.Api.csproj")))
                {
                    return nestedProjectDirectory;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Không tìm thấy thư mục dự án HrService.Api để nạp cấu hình design time.");
    }
}
