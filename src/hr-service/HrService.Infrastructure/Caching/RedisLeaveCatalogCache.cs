using HrService.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace HrService.Infrastructure.Caching;

public sealed class RedisLeaveCatalogCache(
    IDistributedCache distributedCache,
    IOptions<CacheOptions> options,
    ILogger<RedisLeaveCatalogCache> logger) : ILeaveCatalogCache
{
    private readonly TimeSpan _expiration = TimeSpan.FromSeconds(options.Value.ExpirationSeconds);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await distributedCache.GetAsync(key, cancellationToken);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis cache read failed for key {CacheKey}; loading from the database.", key);
            return default;
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "Redis cache read timed out for key {CacheKey}; loading from the database.", key);
            return default;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Redis cache entry {CacheKey} contains invalid JSON; loading from the database.", key);
            await RemoveAsync(key, cancellationToken);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            await distributedCache.SetAsync(
                key,
                bytes,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _expiration
                },
                cancellationToken);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis cache write failed for key {CacheKey}; serving the database result.", key);
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "Redis cache write timed out for key {CacheKey}; serving the database result.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await distributedCache.RemoveAsync(key, cancellationToken);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis cache invalidation failed for key {CacheKey}.", key);
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "Redis cache invalidation timed out for key {CacheKey}.", key);
        }
    }
}
