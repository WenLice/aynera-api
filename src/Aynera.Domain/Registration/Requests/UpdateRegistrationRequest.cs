using Aynera.Domain.Answers.Records;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Preferences.Enums;

namespace Aynera.Domain.Registration.Requests;

/// <summary>
/// One registration page's worth of answers. Every field is optional: the app sends only what the
/// page in front of the member collects, and the server merges it into whatever is already stored.
/// <para>
/// A partial write, unlike <c>UpdateMemberProfileRequest</c> and
/// <c>UpdateMemberPreferencesRequest</c>, which are full replaces. The values sent are validated
/// exactly as strictly as they would be on those; omission is allowed, nonsense is not. Presence of
/// a required set is checked only when that set is promoted to its own table.
/// </para>
/// </summary>
/// <param name="City">A city name from the shared catalog; resolved to its canonical spelling when the profile is created.</param>
/// <param name="GenderIsPublic">Whether the gender shows on the profile. False hides it; matching still uses it.</param>
/// <param name="MaxAgeIsOpen">
/// Send true when the member drags the slider to its ceiling, meaning "<c>minAge</c> and older".
/// It exists because a null <c>maxAge</c> already means "not sent" in a partial write, so there
/// would otherwise be no way to move an upper end back to open once one had been set.
/// </param>
/// <param name="Prompts">
/// The full list of chosen prompts with any typed answers, in order. Replaces the stored list; a
/// recording for a prompt no longer in it is removed.
/// </param>
/// <param name="Dealbreaker">Free text from the profile editor. An empty string clears it.</param>
/// <param name="Rhythm">The three rhythm answers, keyed by question. Replaces the stored set.</param>
/// <param name="NotificationsOn">
/// The notifications page: true for "Yes, let me know", false for "Not now". Sets every notification
/// switch in the member's settings at once; each can be changed on its own later in Settings.
/// </param>
public sealed record UpdateRegistrationRequest(
    string? Name = null,
    string? Nickname = null,
    Gender? Gender = null,
    bool? GenderIsPublic = null,
    DateOnly? DateOfBirth = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? City = null,
    string? Work = null,
    InterestedIn? InterestedIn = null,
    int? MinAge = null,
    int? MaxAge = null,
    bool? MaxAgeIsOpen = null,
    bool? AgeIsFlexible = null,
    RelationshipTrack? Track = null,
    RelationshipOutcome? Outcome = null,
    IReadOnlyDictionary<string, MemberAnswer>? Lifestyle = null,
    IReadOnlyDictionary<string, MemberAnswer>? Beliefs = null,
    IReadOnlyList<string>? Vibe = null,
    IReadOnlyList<MemberPromptAnswer>? Prompts = null,
    string? Dealbreaker = null,
    IReadOnlyDictionary<string, string>? Rhythm = null,
    bool? NotificationsOn = null)
{
    /// <summary>True when the request would change nothing, which is refused rather than ignored.</summary>
    public bool IsEmpty =>
        Name is null
        && Nickname is null
        && Gender is null
        && GenderIsPublic is null
        && DateOfBirth is null
        && HeightCm is null
        && Hometown is null
        && City is null
        && Work is null
        && InterestedIn is null
        && MinAge is null
        && MaxAge is null
        && MaxAgeIsOpen is null
        && AgeIsFlexible is null
        && Track is null
        && Outcome is null
        && Lifestyle is null
        && Beliefs is null
        && Vibe is null
        && Prompts is null
        && Dealbreaker is null
        && Rhythm is null
        && NotificationsOn is null;

    /// <summary>True when the page touches the answers table.</summary>
    public bool HasProfileAnswers =>
        Lifestyle is not null
        || Beliefs is not null
        || Vibe is not null
        || Prompts is not null
        || Dealbreaker is not null
        || Rhythm is not null;
}
