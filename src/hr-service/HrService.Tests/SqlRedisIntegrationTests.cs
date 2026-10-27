using HrService.Application.Abstractions;
using HrService.Application.Dtos;
using HrService.Application.Services;
using HrService.Infrastructure.Caching;
using HrService.Infrastructure.Persistence;
using HrService.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace HrService.Tests;

public sealed class SqlRedisIntegrationTests : IClassFixture<SqlRedisIntegrationFixture>
{
    private readonly SqlRedisIntegrationFixture _fixture;

    public SqlRedisIntegrationTests(SqlRedisIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [SqlRedisFact]
    public async Task LeaveCatalog_PersistsToSqlCachesInRedisAndInvalidatesAfterUpdate()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = _fixture.RedisConnectionString;
        });
        await using var serviceProvider = services.BuildServiceProvider();
        var distributedCache = serviceProvider.GetRequiredService<IDistributedCache>();
        var catalogCache = new RedisLeaveCatalogCache(
            distributedCache,
            Options.Create(new CacheOptions { ExpirationSeconds = 120 }),
            NullLogger<RedisLeaveCatalogCache>.Instance);
        var contextOptions = new DbContextOptionsBuilder<HrDbContext>()
            .UseSqlServer(_fixture.DatabaseConnectionString)
            .Options;

        await using (var context = new HrDbContext(contextOptions))
        {
            var repository = new LeaveCatalogRepository(context);
            var service = new LeaveCatalogService(
                repository,
                catalogCache,
                new UnitOfWork(context));

            var leaveType = await service.CreateLeaveTypeAsync(
                new CreateLeaveTypeRequest("INTEGRATION", "Integration leave", true, true));

            var leaveTypes = await service.GetLeaveTypesAsync(includeInactive: false);
            Assert.Contains(leaveTypes, item => item.Id == leaveType.Id);
            Assert.NotNull(await distributedCache.GetAsync("hr:catalog:leave-types"));

            await service.UpdateLeaveTypeAsync(
                leaveType.Id,
                new UpdateLeaveTypeRequest("Updated integration leave", true, true, true));
            Assert.Null(await distributedCache.GetAsync("hr:catalog:leave-types"));

            var updatedLeaveType = await service.GetLeaveTypeByIdAsync(leaveType.Id);
            Assert.Equal("Updated integration leave", updatedLeaveType.Name);
            Assert.NotNull(await distributedCache.GetAsync("hr:catalog:leave-types"));

            var policy = await service.CreateLeavePolicyAsync(
                new LeavePolicyRequest(leaveType.Id, 2098, 12m));
            var policies = await service.GetLeavePoliciesAsync(2098);
            Assert.Contains(policies, item => item.Id == policy.Id);
            Assert.NotNull(await distributedCache.GetAsync("hr:catalog:leave-policies"));

            var holiday = await service.CreateHolidayAsync(
                new HolidayRequest(new DateOnly(2098, 1, 1), "Integration holiday"));
            var holidays = await service.GetHolidaysAsync(
                new DateOnly(2098, 1, 1),
                new DateOnly(2098, 12, 31));
            Assert.Contains(holidays, item => item.Id == holiday.Id);
            Assert.NotNull(await distributedCache.GetAsync("hr:catalog:holidays"));
        }

        await using var verificationContext = new HrDbContext(contextOptions);
        Assert.True(await verificationContext.LeaveTypes.AnyAsync(item => item.Code == "INTEGRATION"));
        Assert.True(await verificationContext.LeavePolicies.AnyAsync(item => item.Year == 2098));
        Assert.True(await verificationContext.Holidays.AnyAsync(
            item => item.HolidayDate == new DateOnly(2098, 1, 1)));
    }
}

public sealed class SqlRedisFactAttribute : FactAttribute
{
    public SqlRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("HR_TEST_SQL_CONNECTION_STRING")) ||
            string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("HR_TEST_REDIS_CONNECTION_STRING")))
        {
            Skip = "Set HR_TEST_SQL_CONNECTION_STRING and HR_TEST_REDIS_CONNECTION_STRING to run SQL/Redis integration tests.";
        }
    }
}

public sealed class SqlRedisIntegrationFixture : IAsyncLifetime
{
    private readonly string? _masterConnectionString =
        Environment.GetEnvironmentVariable("HR_TEST_SQL_CONNECTION_STRING");
    private bool _databaseCreated;

    public bool IsAvailable { get; private set; }
    public string DatabaseName { get; } = $"HrServiceIntegration_{Guid.NewGuid():N}";
    public string DatabaseConnectionString { get; private set; } = string.Empty;
    public string RedisConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var redisEndpoint = Environment.GetEnvironmentVariable("HR_TEST_REDIS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(_masterConnectionString) ||
            string.IsNullOrWhiteSpace(redisEndpoint))
        {
            return;
        }

        var masterConnection = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
            Encrypt = false,
            TrustServerCertificate = true
        };
        var databaseConnection = new SqlConnectionStringBuilder(masterConnection.ConnectionString)
        {
            InitialCatalog = DatabaseName
        };
        DatabaseConnectionString = databaseConnection.ConnectionString;
        RedisConnectionString = redisEndpoint.Contains("defaultDatabase=", StringComparison.OrdinalIgnoreCase)
            ? redisEndpoint
            : $"{redisEndpoint.TrimEnd(',')},defaultDatabase=15";

        await using (var connection = new SqlConnection(masterConnection.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await command.ExecuteNonQueryAsync();
            _databaseCreated = true;
        }

        try
        {
            var options = new DbContextOptionsBuilder<HrDbContext>()
                .UseSqlServer(DatabaseConnectionString)
                .Options;
            await using var context = new HrDbContext(options);
            await context.Database.MigrateAsync();
            IsAvailable = true;
        }
        catch
        {
            await DropDatabaseAsync(masterConnection.ConnectionString);
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_databaseCreated && !string.IsNullOrWhiteSpace(_masterConnectionString))
        {
            var masterConnection = new SqlConnectionStringBuilder(_masterConnectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 5,
                Encrypt = false,
                TrustServerCertificate = true
            };
            await DropDatabaseAsync(masterConnection.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(RedisConnectionString))
        {
            using var redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
            var database = redis.GetDatabase();
            await database.KeyDeleteAsync(
            [
                "hr:catalog:leave-types",
                "hr:catalog:leave-policies",
                "hr:catalog:holidays"
            ]);
        }
    }

    private async Task DropDatabaseAsync(string masterConnectionString)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]";
        await command.ExecuteNonQueryAsync();
        _databaseCreated = false;
    }
}
