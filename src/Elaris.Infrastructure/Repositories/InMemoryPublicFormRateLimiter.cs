using System.Collections.Concurrent;
using Elaris.Application.Features.PublicForms.Repositories;

namespace Elaris.Infrastructure.Repositories;

public sealed class InMemoryPublicFormRateLimiter : IPublicFormRateLimiter
{
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _windows = new();

    public Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireAsync(
        string bucket,
        string? clientIp,
        int maxPerHour,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(clientIp) || maxPerHour <= 0)
        {
            return Task.FromResult<(bool, int?)>((true, null));
        }

        var key = $"{bucket}:{clientIp}";
        var now = DateTimeOffset.UtcNow;
        var entry = _windows.AddOrUpdate(
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

        if (entry.Count > maxPerHour)
        {
            var retry = (int)Math.Ceiling((entry.WindowStart.AddHours(1) - now).TotalSeconds);
            return Task.FromResult<(bool, int?)>((false, Math.Max(1, retry)));
        }

        return Task.FromResult<(bool, int?)>((true, null));
    }
}
