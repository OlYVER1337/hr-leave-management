namespace HrService.Infrastructure.Caching;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public bool Enabled { get; set; }
    public int ExpirationSeconds { get; set; } = 300;
}
