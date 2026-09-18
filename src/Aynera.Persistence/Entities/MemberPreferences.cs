using Aynera.Domain.Preferences.Enums;
using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

/// <summary>
/// A member's matching hard filters (MATCHMAKING-RULES §6). One row per member, like
/// <see cref="MemberProfile"/>. City is not here — it is read from the profile, so members
/// and venues keep sharing one city key.
/// </summary>
public sealed class MemberPreferences : ISoftDeletable
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Stored as the member's choice, not the expanded gender set, so that widening what
    /// <see cref="InterestedIn.Everyone"/> covers carries existing rows with it.
    /// </summary>
    public InterestedIn InterestedIn { get; set; }

    public int MinAge { get; set; }
    public int MaxAge { get; set; }

    /// <summary>Widens the range by two years at each end when the pair is evaluated.</summary>
    public bool AgeIsFlexible { get; set; }

    /// <summary>The track is implied by this, never stored alongside it.</summary>
    public IntentOutcome IntentOutcome { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
