using Aynera.Domain.Auth.Records;

namespace Aynera.Application.Features.Auth.Repositories;

public enum OtpConsumeOutcome
{
    Consumed = 0,
    NotFound = 1,
    Invalid = 2,
    Locked = 3
}

public interface IOtpChallengeRepository
{
    Task<(bool Allowed, int? RetryAfterSeconds)> TryAcquireRequestSlotAsync(
        string channel,
        string destination,
        string? clientIp,
        CancellationToken cancellationToken);

    Task StoreAsync(OtpChallenge challenge, TimeSpan ttl, CancellationToken cancellationToken);
    Task<OtpChallenge?> GetAsync(string channel, string destination, CancellationToken cancellationToken);
    Task<OtpConsumeOutcome> TryConsumeAsync(
        string channel,
        string destination,
        string codeHash,
        string expectedPurpose,
        string expectedAudience,
        CancellationToken cancellationToken);
    Task RemoveAsync(string channel, string destination, CancellationToken cancellationToken);
}
