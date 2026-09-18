using System.Text.Json;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Records;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Aynera.Infrastructure.Repositories;

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
        var payload = JsonSerializer.Serialize(
            RedisOtpDocument.FromChallenge(challenge),
            JsonOptions);
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

        var document = JsonSerializer.Deserialize<RedisOtpDocument>((string)value!, JsonOptions);
        return document?.ToChallenge();
    }

    public async Task<OtpConsumeOutcome> TryConsumeAsync(
        string channel,
        string destination,
        string codeHash,
        string expectedPurpose,
        string expectedAudience,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var result = (int)(long)await db.ScriptEvaluateAsync(
            ConsumeScript,
            new RedisKey[] { ChallengeKey(channel, destination) },
            new RedisValue[]
            {
                codeHash,
                expectedPurpose,
                expectedAudience,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                _options.MaxAttempts
            });

        return result switch
        {
            1 => OtpConsumeOutcome.Consumed,
            2 => OtpConsumeOutcome.Invalid,
            3 => OtpConsumeOutcome.Locked,
            _ => OtpConsumeOutcome.NotFound
        };
    }

    public async Task RemoveAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(ChallengeKey(channel, destination));
    }

    private static string ChallengeKey(string channel, string destination) => $"otp:{channel}:{destination}";

    private static string RateLimitKey(string channel, string destination) => $"otp:rl:{channel}:{destination}";

    private const string ConsumeScript = """
        local key = KEYS[1]
        local submittedHash = string.lower(ARGV[1])
        local purpose = ARGV[2]
        local audience = ARGV[3]
        local nowUnix = tonumber(ARGV[4])
        local maxAttempts = tonumber(ARGV[5])

        local raw = redis.call('GET', key)
        if not raw then
          return 0
        end

        local ok, challenge = pcall(cjson.decode, raw)
        if not ok or type(challenge) ~= 'table' then
          return 0
        end

        if challenge.purpose ~= purpose or challenge.audience ~= audience then
          return 0
        end

        local exp = tonumber(challenge.expiresAtUnix)
        if not exp or exp <= nowUnix then
          return 0
        end

        local attempts = tonumber(challenge.attempts) or 0
        if attempts >= maxAttempts then
          redis.call('DEL', key)
          return 3
        end

        local storedHash = string.lower(tostring(challenge.codeHash or ''))
        if storedHash == submittedHash then
          redis.call('DEL', key)
          return 1
        end

        attempts = attempts + 1
        if attempts >= maxAttempts then
          redis.call('DEL', key)
          return 3
        end

        challenge.attempts = attempts
        local encoded = cjson.encode(challenge)
        local ttl = redis.call('PTTL', key)
        if ttl > 0 then
          redis.call('SET', key, encoded, 'PX', ttl)
        elseif ttl == -1 then
          redis.call('SET', key, encoded)
        else
          return 0
        end
        return 2
        """;

    private sealed record RedisOtpDocument(
        string Channel,
        string Destination,
        string CodeHash,
        int Attempts,
        DateTimeOffset ExpiresAtUtc,
        long ExpiresAtUnix,
        string Purpose,
        string Audience)
    {
        public static RedisOtpDocument FromChallenge(OtpChallenge challenge) =>
            new(
                challenge.Channel,
                challenge.Destination,
                challenge.CodeHash,
                challenge.Attempts,
                challenge.ExpiresAtUtc,
                challenge.ExpiresAtUtc.ToUnixTimeSeconds(),
                challenge.Purpose,
                challenge.Audience);

        public OtpChallenge ToChallenge() =>
            new(Channel, Destination, CodeHash, Attempts, ExpiresAtUtc, Purpose, Audience);
    }
}
