using System.Text.Json;
using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Domain.Auth.Records;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Elaris.Infrastructure.Repositories;

public sealed class RedisOtpChallengeRepository : IOtpChallengeRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly OtpOptions _options;

    public RedisOtpChallengeRepository(IConnectionMultiplexer redis, IOptions<OtpOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    public async Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireRequestSlotAsync(
        string channel,
        string destination,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();

        var identifierKey = RateLimitKey(channel, destination);
        var identifierCount = await db.StringIncrementAsync(identifierKey);
        if (identifierCount == 1)
        {
            await db.KeyExpireAsync(identifierKey, TimeSpan.FromHours(1));
        }

        if (identifierCount > _options.MaxRequestsPerPhonePerHour)
        {
            var ttl = await db.KeyTimeToLiveAsync(identifierKey);
            return (false, (int?)ttl?.TotalSeconds ?? 3600);
        }

        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            var ipKey = $"otp:rl:ip:{clientIp}";
            var ipCount = await db.StringIncrementAsync(ipKey);
            if (ipCount == 1)
            {
                await db.KeyExpireAsync(ipKey, TimeSpan.FromHours(1));
            }

            if (ipCount > _options.MaxRequestsPerIpPerHour)
            {
                var ttl = await db.KeyTimeToLiveAsync(ipKey);
                return (false, (int?)ttl?.TotalSeconds ?? 3600);
            }
        }

        return (true, null);
    }

    public async Task StoreAsync(OtpChallenge challenge, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(challenge, JsonOptions);
        await db.StringSetAsync(ChallengeKey(challenge.Channel, challenge.Destination), payload, ttl);
    }

    public async Task<OtpChallenge?> GetAsync(
        string channel,
        string destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(ChallengeKey(channel, destination));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<OtpChallenge>((string)value!, JsonOptions);
    }

    public async Task<bool> IncrementAttemptsAsync(
        string channel,
        string destination,
        CancellationToken cancellationToken)
    {
        var challenge = await GetAsync(channel, destination, cancellationToken);
        if (challenge is null)
        {
            return false;
        }

        var updated = challenge with { Attempts = challenge.Attempts + 1 };
        if (updated.Attempts >= _options.MaxAttempts)
        {
            await RemoveAsync(channel, destination, cancellationToken);
            return false;
        }

        var db = _redis.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync(ChallengeKey(channel, destination))
            ?? TimeSpan.FromSeconds(_options.TtlSeconds);
        await StoreAsync(updated, ttl, cancellationToken);
        return true;
    }

    public async Task RemoveAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(ChallengeKey(channel, destination));
    }

    private static string ChallengeKey(string channel, string destination) => $"otp:{channel}:{destination}";

    private static string RateLimitKey(string channel, string destination) => $"otp:rl:{channel}:{destination}";
}
