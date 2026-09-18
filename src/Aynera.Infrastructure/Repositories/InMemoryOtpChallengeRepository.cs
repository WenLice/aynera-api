using System.Collections.Concurrent;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Statics;
using Microsoft.Extensions.Options;

namespace Aynera.Infrastructure.Repositories;

/// <summary>
/// In-memory OTP repository for tests and environments without Redis.
/// </summary>
public sealed class InMemoryOtpChallengeRepository : IOtpChallengeRepository
{
    private readonly ConcurrentDictionary<string, (OtpChallenge Challenge, DateTimeOffset ExpiresAt)> _challenges = new();
    private readonly ConcurrentDictionary<string, object> _challengeLocks = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _identifierLimits = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _ipLimits = new();
    private readonly OtpOptions _options;

    public InMemoryOtpChallengeRepository(IOptions<OtpOptions> options)
    {
        _options = options.Value;
    }

    public Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireRequestSlotAsync(
        string channel,
        string destination,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        if (!TryIncrement(
                _identifierLimits,
                RateLimitKey(channel, destination),
                _options.MaxRequestsPerPhonePerHour,
                out var identifierRetry))
        {
            return Task.FromResult<(bool, int?)>((false, identifierRetry));
        }

        if (!string.IsNullOrWhiteSpace(clientIp)
            && !TryIncrement(_ipLimits, clientIp, _options.MaxRequestsPerIpPerHour, out var ipRetry))
        {
            return Task.FromResult<(bool, int?)>((false, ipRetry));
        }

        return Task.FromResult<(bool, int?)>((true, null));
    }

    public Task StoreAsync(OtpChallenge challenge, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = ChallengeKey(challenge.Channel, challenge.Destination);
        lock (LockFor(key))
        {
            _challenges[key] = (challenge, DateTimeOffset.UtcNow.Add(ttl));
        }

        return Task.CompletedTask;
    }

    public Task<OtpChallenge?> GetAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        var key = ChallengeKey(channel, destination);
        lock (LockFor(key))
        {
            return Task.FromResult(ReadCurrent(key));
        }
    }

    public Task<OtpConsumeOutcome> TryConsumeAsync(
        string channel,
        string destination,
        string codeHash,
        string expectedPurpose,
        string expectedAudience,
        CancellationToken cancellationToken)
    {
        var key = ChallengeKey(channel, destination);
        lock (LockFor(key))
        {
            var challenge = ReadCurrent(key);
            if (challenge is null
                || challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow
                || !string.Equals(challenge.Purpose, expectedPurpose, StringComparison.Ordinal)
                || !string.Equals(challenge.Audience, expectedAudience, StringComparison.Ordinal))
            {
                return Task.FromResult(OtpConsumeOutcome.NotFound);
            }

            if (challenge.Attempts >= _options.MaxAttempts)
            {
                _challenges.TryRemove(key, out _);
                return Task.FromResult(OtpConsumeOutcome.Locked);
            }

            if (!SecureEquals.Hex(codeHash, challenge.CodeHash))
            {
                var attempts = challenge.Attempts + 1;
                if (attempts >= _options.MaxAttempts)
                {
                    _challenges.TryRemove(key, out _);
                    return Task.FromResult(OtpConsumeOutcome.Locked);
                }

                if (_challenges.TryGetValue(key, out var entry))
                {
                    _challenges[key] = (challenge with { Attempts = attempts }, entry.ExpiresAt);
                }

                return Task.FromResult(OtpConsumeOutcome.Invalid);
            }

            _challenges.TryRemove(key, out _);
            return Task.FromResult(OtpConsumeOutcome.Consumed);
        }
    }

    public Task RemoveAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        var key = ChallengeKey(channel, destination);
        lock (LockFor(key))
        {
            _challenges.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    private OtpChallenge? ReadCurrent(string key)
    {
        if (!_challenges.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _challenges.TryRemove(key, out _);
            return null;
        }

        return entry.Challenge;
    }

    private object LockFor(string key) => _challengeLocks.GetOrAdd(key, static _ => new object());

    private static string ChallengeKey(string channel, string destination) => $"{channel}:{destination}";

    private static string RateLimitKey(string channel, string destination) => $"{channel}:{destination}";

    private static bool TryIncrement(
        ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> map,
        string key,
        int max,
        out int? retryAfterSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = map.AddOrUpdate(
            key,
            _ => (1, now),
            (_, existing) =>
            {
                if (now - existing.WindowStart >= TimeSpan.FromHours(1))
                {
                    return (1, now);
                }

                return (existing.Count + 1, existing.WindowStart);
            });

        if (entry.Count > max)
        {
            retryAfterSeconds = (int)Math.Max(1, (entry.WindowStart.AddHours(1) - now).TotalSeconds);
            return false;
        }

        retryAfterSeconds = null;
        return true;
    }
}
