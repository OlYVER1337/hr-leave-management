using HrService.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HrService.Tests;

public sealed class RedisLeaveCatalogCacheTests
{
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
}
