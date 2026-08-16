using System.Collections.Concurrent;
using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Domain.Auth.Records;
using Microsoft.Extensions.Options;

namespace Elaris.Infrastructure.Repositories;

/// <summary>
/// In-memory OTP repository for tests and environments without Redis.
/// </summary>
public sealed class InMemoryOtpChallengeRepository : IOtpChallengeRepository
{
    private readonly ConcurrentDictionary<string, (OtpChallenge Challenge, DateTimeOffset ExpiresAt)> _challenges = new();
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
        _challenges[ChallengeKey(challenge.Channel, challenge.Destination)] = (challenge, DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    public Task<OtpChallenge?> GetAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        if (!_challenges.TryGetValue(ChallengeKey(channel, destination), out var entry))
        {
            return Task.FromResult<OtpChallenge?>(null);
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _challenges.TryRemove(ChallengeKey(channel, destination), out _);
            return Task.FromResult<OtpChallenge?>(null);
        }

        return Task.FromResult<OtpChallenge?>(entry.Challenge);
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
        var key = ChallengeKey(channel, destination);
        if (updated.Attempts >= _options.MaxAttempts)
        {
            await RemoveAsync(channel, destination, cancellationToken);
            return false;
        }

        if (_challenges.TryGetValue(key, out var entry))
        {
            _challenges[key] = (updated, entry.ExpiresAt);
        }

        return true;
    }

    public Task RemoveAsync(string channel, string destination, CancellationToken cancellationToken)
    {
        _challenges.TryRemove(ChallengeKey(channel, destination), out _);
        return Task.CompletedTask;
    }

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
