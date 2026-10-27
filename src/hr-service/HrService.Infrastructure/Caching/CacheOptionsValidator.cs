using Microsoft.Extensions.Options;

namespace HrService.Infrastructure.Caching;

public sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    public ValidateOptionsResult Validate(string? name, CacheOptions options)
    {
        return options.ExpirationSeconds is >= 1 and <= 3600
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Cache:ExpirationSeconds must be between 1 and 3600.");
    }
}
