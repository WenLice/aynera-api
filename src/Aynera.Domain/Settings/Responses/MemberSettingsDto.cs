namespace Aynera.Domain.Settings.Responses;

/// <summary>
/// The member's notification switches, pause, and field visibility. Notification fields are null
/// until the member answers; a field missing from <paramref name="Visibility"/> is shown.
/// </summary>
public sealed record MemberSettingsDto(
    bool? NotifyIntroductions,
    bool? NotifyReplies,
    bool? NotifyWeekendSurprise,
    bool IntroductionsPaused,
    DateTimeOffset? PausedAtUtc,
    IReadOnlyDictionary<string, bool> Visibility);
