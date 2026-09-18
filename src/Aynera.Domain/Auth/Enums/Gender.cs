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
    Other = 2,

    /// <summary>
    /// An explicit decline, not a missing answer. Distinct from <see cref="Other"/> so
    /// matchmaking can tell "I'd rather not say" from "third gender / transgender".
    /// </summary>
    PreferNotToSay = 3
}
