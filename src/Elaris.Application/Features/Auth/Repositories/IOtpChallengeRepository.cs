using Elaris.Domain.Auth.Records;

namespace Elaris.Application.Features.Auth.Repositories;

public interface IOtpChallengeRepository
{
    Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireRequestSlotAsync(
        string channel,
        string destination,
        string? clientIp,
        CancellationToken cancellationToken);

    Task StoreAsync(OtpChallenge challenge, TimeSpan ttl, CancellationToken cancellationToken);
    Task<OtpChallenge?> GetAsync(string channel, string destination, CancellationToken cancellationToken);
    Task<bool> IncrementAttemptsAsync(string channel, string destination, CancellationToken cancellationToken);
    Task RemoveAsync(string channel, string destination, CancellationToken cancellationToken);
}
