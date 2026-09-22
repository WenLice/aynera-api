namespace Aynera.Domain.Auth.Enums;

/// <summary>
/// Stored as its name (see the <c>MemberProfiles.Gender</c> string conversion), so adding a
/// value needs no migration. Accepted on the wire by name or by number.
/// </summary>
public enum Gender
{
    Male = 0,
    Female = 1,

    /// <summary>Third gender / transgender — the app offers it under that label.</summary>
    ThirdGender = 2,

    /// <summary>
    /// Legacy. "Prefer not to say" is about display, not identity, so it is now
    /// <c>MemberProfile.GenderIsPublic</c> instead — a member states a gender for the
    /// reciprocal hard filter and chooses separately whether it appears on their profile.
    /// No client offers this value and nothing new writes it; it stays so the rows written
    /// while it was offered still parse, rather than having a gender invented for them.
    /// </summary>
    PreferNotToSay = 3
}
