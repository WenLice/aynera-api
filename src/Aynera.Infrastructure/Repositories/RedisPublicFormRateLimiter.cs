using Aynera.Application.Features.PublicForms.Repositories;
using StackExchange.Redis;

namespace Aynera.Infrastructure.Repositories;

public sealed class RedisPublicFormRateLimiter : IPublicFormRateLimiter
{
    private readonly IConnectionMultiplexer _redis;

    public RedisPublicFormRateLimiter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireAsync(
        string bucket,
        string? clientIp,
        int maxPerHour,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(clientIp) || maxPerHour <= 0)
        {
            return (true, null);
        }

        var db = _redis.GetDatabase();
        var key = $"public-form:rl:{bucket}:{clientIp}";
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromHours(1));
        }

        if (count > maxPerHour)
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            return (false, (int?)ttl?.TotalSeconds ?? 3600);
        }

        return (true, null);
    }
}
