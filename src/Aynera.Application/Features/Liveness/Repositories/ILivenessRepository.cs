using Aynera.Domain.Liveness.Records;

namespace Aynera.Application.Features.Liveness.Repositories;

public interface ILivenessRepository
{
    Task AddSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken);

    Task<LivenessSessionRecord?> FindSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task UpdateSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken);

    /// <summary>The member's most recent session that reached a verdict, if any.</summary>
    Task<LivenessSessionRecord?> FindLatestCompletedAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the reference frame as the member's liveness selfie, replacing any earlier one, with
    /// the face-match result that eligibility reads.
    /// </summary>
    Task SaveSelfieAsync(
        Guid userId,
        byte[] jpeg,
        string faceMatchStatus,
        decimal? similarity,
        CancellationToken cancellationToken);
}
