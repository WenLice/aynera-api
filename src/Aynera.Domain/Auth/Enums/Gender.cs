namespace Aynera.Domain.Auth.Enums;

/// <summary>
/// The three genders a member can state. Every member states one — the reciprocal hard filter
/// needs it on both sides — and chooses separately whether it shows on their profile (the
/// <c>gender</c> entry in <c>MemberSettings.Visibility</c>).
/// <para>
/// Stored as its name (see the <c>MemberProfiles.Gender</c> string conversion). Accepted on the
/// wire by name or by number.
/// </para>
/// </summary>
public enum Gender
{
    Male = 0,
    Female = 1,

    /// <summary>Third gender / transgender — the app offers it under that label.</summary>
    ThirdGender = 2,
}
