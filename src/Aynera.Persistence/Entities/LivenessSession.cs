using Aynera.Domain.Liveness.Enums;

namespace Aynera.Persistence.Entities;

/// <summary>
/// One face-liveness session and who started it. The provider does not know our members, so this
/// row is what stops one member completing — or reading — another member's session.
/// </summary>
public sealed class LivenessSession
{
    /// <summary>The provider's session id.</summary>
    public string SessionId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public LivenessOutcome Outcome { get; set; } = LivenessOutcome.Pending;
    public decimal? Confidence { get; set; }
    public decimal? Similarity { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
