using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

/// <summary>
/// A member's choices about how Aynera treats them — what reaches them, whether they are taking a
/// break, and which profile fields other members may see. One row per member, created the first
/// time anything is set; a missing row means every default applies.
/// </summary>
public sealed class MemberSettings : ISoftDeletable
{
    public Guid UserId { get; set; }

    /// <summary>A new introduction is ready. Null until the member answers.</summary>
    public bool? NotifyIntroductions { get; set; }

    /// <summary>Someone wrote back. Null until the member answers.</summary>
    public bool? NotifyReplies { get; set; }

    /// <summary>A Weekend Surprise drop opens. Null until the member answers.</summary>
    public bool? NotifyWeekendSurprise { get; set; }

    /// <summary>
    /// The member stepped away. A real column rather than part of a document, because matching
    /// will filter on it.
    /// </summary>
    public bool IntroductionsPaused { get; set; }

    /// <summary>When the current pause began; null when not paused.</summary>
    public DateTimeOffset? PausedAtUtc { get; set; }

    /// <summary>
    /// Which profile fields show, as <c>{"gender":false,"lifestyle.drink":true}</c>. A field with no
    /// entry is shown. A document rather than columns because the question list changes freely and a
    /// new field must never need a migration to be hideable.
    /// </summary>
    public string Visibility { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
