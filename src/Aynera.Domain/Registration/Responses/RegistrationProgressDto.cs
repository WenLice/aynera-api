using Aynera.Domain.Answers.Responses;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Preferences.Responses;
using Aynera.Domain.Registration.Records;
using Aynera.Domain.Settings.Responses;

namespace Aynera.Domain.Registration.Responses;

/// <summary>
/// Where a member stands in registration: what they have answered, which steps that satisfies, and
/// the first step still outstanding.
/// <para>
/// Returned from both the read and every page write, so the app can prefill, reopen at the right
/// step, and notice the moment the profile is created — without a second round trip and without
/// holding a copy of the completeness rule.
/// </para>
/// </summary>
/// <param name="Answers">
/// What is still only in the draft. A group drops out of here once promoted, because its own table
/// owns it from then on — read it from <paramref name="Profile"/> or <paramref name="Preferences"/>.
/// </param>
/// <param name="NextStep">Null when every step in the flow is satisfied.</param>
/// <param name="Profile">Non-null once the personal details were complete and the profile was created.</param>
/// <param name="Preferences">Non-null once the hard filters were complete and the row was created.</param>
/// <param name="Settings">Notification switches, pause and field visibility, once any was set.</param>
public sealed record RegistrationProgressDto(
    RegistrationAnswers Answers,
    IReadOnlyList<string> Completed,
    string? NextStep,
    MemberProfileDto? Profile = null,
    MemberPreferencesDto? Preferences = null,
    MemberProfileAnswersDto? ProfileAnswers = null,
    MemberSettingsDto? Settings = null);
