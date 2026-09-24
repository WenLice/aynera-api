namespace Aynera.Domain.Settings.Requests;

/// <summary>
/// A partial change to the member's settings: null leaves a field alone. <paramref name="Visibility"/>
/// merges per field, so sending <c>{"gender": false}</c> changes only the gender.
/// </summary>
public sealed record UpdateMemberSettingsRequest(
    bool? NotifyIntroductions = null,
    bool? NotifyReplies = null,
    bool? NotifyWeekendSurprise = null,
    bool? IntroductionsPaused = null,
    IReadOnlyDictionary<string, bool>? Visibility = null)
{
    public bool IsEmpty =>
        NotifyIntroductions is null
        && NotifyReplies is null
        && NotifyWeekendSurprise is null
        && IntroductionsPaused is null
        && Visibility is null;
}
