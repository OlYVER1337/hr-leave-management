using HrService.Application.Abstractions;
using HrService.Application.Services;
using HrService.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HrService.Domain.Entities;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HrService.Tests;

public sealed class RedisLeaveCatalogCacheTests
{
    // TTL mặc định của catalog cache phải là 30 phút nếu không cấu hình khác.
    [Fact]
    public void CacheOptions_DefaultExpirationIsThirtyMinutes()
    {
        Assert.Equal(1800, new CacheOptions().ExpirationSeconds);
    }

    // Redis timeout được xem là cache miss để service có thể fallback sang SQL.
    [Fact]
    public async Task GetAsync_ReturnsCacheMissWhenRedisTimesOut()
    {
        var distributedCache = new Mock<IDistributedCache>();
        distributedCache
            .Setup(cache => cache.GetAsync("catalog-key", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timed out."));
        var cache = new RedisLeaveCatalogCache(
            distributedCache.Object,
            Options.Create(new CacheOptions { ExpirationSeconds = 300 }),
            NullLogger<RedisLeaveCatalogCache>.Instance);

        var result = await cache.GetAsync<string>("catalog-key");

        Assert.Null(result);
    }

    // Lỗi ghi cache không được làm hỏng thao tác nghiệp vụ vốn đã có dữ liệu từ SQL.
    [Fact]
    public async Task SetAsync_DoesNotThrowWhenRedisTimesOut()
    {
        var distributedCache = new Mock<IDistributedCache>();
        distributedCache
            .Setup(cache => cache.SetAsync(
                "catalog-key",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timed out."));
        var cache = new RedisLeaveCatalogCache(
            distributedCache.Object,
            Options.Create(new CacheOptions { ExpirationSeconds = 300 }),
            NullLogger<RedisLeaveCatalogCache>.Instance);

        await cache.SetAsync("catalog-key", "value");
    }

    // Khi Redis timeout, HR phải đọc leave types từ SQL thay vì trả lỗi.
    [Fact]
    public async Task GetLeaveTypesAsync_LoadsFromSqlWhenRedisIsUnavailable()
    {
        var distributedCache = new Mock<IDistributedCache>();
        distributedCache
            .Setup(cache => cache.GetAsync("hr:catalog:leave-types", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timed out."));
        distributedCache
            .Setup(cache => cache.SetAsync(
                "hr:catalog:leave-types",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timed out."));
        var repository = new Mock<ILeaveCatalogRepository>();
        repository
            .Setup(item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveType { Id = 1, Code = "ANNUAL", Name = "Annual", IsActive = true }]);
        var service = new LeaveCatalogService(
            repository.Object,
            new RedisLeaveCatalogCache(
                distributedCache.Object,
                Options.Create(new CacheOptions { ExpirationSeconds = 300 }),
                NullLogger<RedisLeaveCatalogCache>.Instance),
            Mock.Of<IUnitOfWork>(),
            TimeProvider.System);

        var result = await service.GetLeaveTypesAsync(includeInactive: false);

        Assert.Equal("ANNUAL", Assert.Single(result).Code);
        repository.Verify(
            item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Trường hợp Redis không kết nối được cũng phải fallback sang SQL.
    [Fact]
    public async Task GetLeaveTypesAsync_LoadsFromSqlWhenRedisConnectionIsUnavailable()
    {
        var distributedCache = new Mock<IDistributedCache>();
        distributedCache
            .Setup(cache => cache.GetAsync("hr:catalog:leave-types", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                "Redis is unavailable."));
        var repository = new Mock<ILeaveCatalogRepository>();
        repository
            .Setup(item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveType { Id = 1, Code = "ANNUAL", Name = "Annual", IsActive = true }]);
        var service = new LeaveCatalogService(
            repository.Object,
            new RedisLeaveCatalogCache(
                distributedCache.Object,
                Options.Create(new CacheOptions { ExpirationSeconds = 300 }),
                NullLogger<RedisLeaveCatalogCache>.Instance),
            Mock.Of<IUnitOfWork>(),
            TimeProvider.System);

        var result = await service.GetLeaveTypesAsync(includeInactive: false);

        Assert.Equal("ANNUAL", Assert.Single(result).Code);
        repository.Verify(
            item => item.ListLeaveTypesAsync(true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Không để lỗi invalidation Redis chặn luồng cập nhật dữ liệu SQL.
    [Fact]
    public async Task RemoveAsync_DoesNotFailWhenRedisIsUnavailable()
    {
        var distributedCache = new Mock<IDistributedCache>();
        distributedCache
            .Setup(cache => cache.RemoveAsync("catalog-key", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timed out."));
        var cache = new RedisLeaveCatalogCache(
            distributedCache.Object,
            Options.Create(new CacheOptions { ExpirationSeconds = 300 }),
            NullLogger<RedisLeaveCatalogCache>.Instance);

        await cache.RemoveAsync("catalog-key");
    }
}
