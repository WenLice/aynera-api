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
    /// <summary>Null is an open upper end — see HardFilters.AgeAccepts.</summary>
    public int? MaxAge { get; set; }

    /// <summary>Widens the range by two years at each end when the pair is evaluated.</summary>
    public bool AgeIsFlexible { get; set; }

    /// <summary>
    /// The track the member chose first. Stored rather than derived so the two-stage choice is
    /// recorded as made, but the write path validates it against <see cref="Outcome"/>, so the
    /// pair can never disagree. Matching filters on the outcome, never on this.
    /// </summary>
    public RelationshipTrack Track { get; set; }

    /// <summary>The child of <see cref="Track"/> that the member picked.</summary>
    public RelationshipOutcome Outcome { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
