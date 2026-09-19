namespace Aynera.Domain.Preferences.Records;

/// <summary>
/// Application-facing projection of a member's matching preferences. Enums travel as their
/// names, matching how <see cref="Auth.Records.MemberProfileRecord"/> carries gender.
/// <see cref="Track"/> is stored rather than derived, but the write path validates it against
/// <see cref="Outcome"/>, so the two always agree.
/// </summary>
public sealed record MemberPreferencesRecord(
    Guid UserId,
    string InterestedIn,
    int MinAge,
    int? MaxAge,
    bool AgeIsFlexible,
    string Track,
    string Outcome);
