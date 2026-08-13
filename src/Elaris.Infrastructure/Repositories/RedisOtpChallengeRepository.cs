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
        string phoneE164,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();

        var phoneKey = $"otp:rl:phone:{phoneE164}";
        var phoneCount = await db.StringIncrementAsync(phoneKey);
        if (phoneCount == 1)
        {
            await db.KeyExpireAsync(phoneKey, TimeSpan.FromHours(1));
        }

        if (phoneCount > _options.MaxRequestsPerPhonePerHour)
        {
            var ttl = await db.KeyTimeToLiveAsync(phoneKey);
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
        await db.StringSetAsync(ChallengeKey(challenge.PhoneE164), payload, ttl);
    }

    public async Task<OtpChallenge?> GetAsync(string phoneE164, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(ChallengeKey(phoneE164));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<OtpChallenge>((string)value!, JsonOptions);
    }

    public async Task<bool> IncrementAttemptsAsync(string phoneE164, CancellationToken cancellationToken)
    {
        var challenge = await GetAsync(phoneE164, cancellationToken);
        if (challenge is null)
        {
            return false;
        }

        var updated = challenge with { Attempts = challenge.Attempts + 1 };
        if (updated.Attempts >= _options.MaxAttempts)
        {
            await RemoveAsync(phoneE164, cancellationToken);
            return false;
        }

        var db = _redis.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync(ChallengeKey(phoneE164)) ?? TimeSpan.FromSeconds(_options.TtlSeconds);
        await StoreAsync(updated, ttl, cancellationToken);
        return true;
    }

    public async Task RemoveAsync(string phoneE164, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(ChallengeKey(phoneE164));
    }

    private static string ChallengeKey(string phoneE164) => $"otp:phone:{phoneE164}";
}
