using Elaris.Domain.Auth.Records;

namespace Elaris.Application.Features.Auth.Repositories;

public interface IOtpChallengeRepository
{
    Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireRequestSlotAsync(
        string phoneE164,
        string? clientIp,
        CancellationToken cancellationToken);

    Task StoreAsync(OtpChallenge challenge, TimeSpan ttl, CancellationToken cancellationToken);
    Task<OtpChallenge?> GetAsync(string phoneE164, CancellationToken cancellationToken);
    Task<bool> IncrementAttemptsAsync(string phoneE164, CancellationToken cancellationToken);
    Task RemoveAsync(string phoneE164, CancellationToken cancellationToken);
}
