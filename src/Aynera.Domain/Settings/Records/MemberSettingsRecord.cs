namespace Aynera.Domain.Settings.Records;

/// <summary>
/// A member's choices about how Aynera treats them: what reaches them, whether they are taking a
/// break, and which profile fields other members may see. One row per member.
/// </summary>
/// <param name="NotifyIntroductions">A new introduction is ready. Null until the member answers.</param>
/// <param name="NotifyReplies">Someone wrote back. Null until the member answers.</param>
/// <param name="NotifyWeekendSurprise">A Weekend Surprise drop opens. Null until the member answers.</param>
/// <param name="IntroductionsPaused">The member stepped away; nobody new is shown their introduction.</param>
/// <param name="PausedAtUtc">When the current pause began; null when not paused.</param>
/// <param name="Visibility">Profile field → shown. A field with no entry is shown (see <c>VisibilityKeys</c>).</param>
public sealed record MemberSettingsRecord(
    Guid UserId,
    bool? NotifyIntroductions,
    bool? NotifyReplies,
    bool? NotifyWeekendSurprise,
    bool IntroductionsPaused,
    DateTimeOffset? PausedAtUtc,
    IReadOnlyDictionary<string, bool> Visibility)
{
    public static MemberSettingsRecord Empty(Guid userId) =>
        new(userId, null, null, null, false, null, new Dictionary<string, bool>());
}
