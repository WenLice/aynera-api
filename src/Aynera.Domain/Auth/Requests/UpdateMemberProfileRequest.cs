using Aynera.Domain.Auth.Enums;

namespace Aynera.Domain.Auth.Requests;

/// <summary>
/// The signed-in member's basic details, written in one go once the app has collected them
/// (its "you", "basics" and "life" steps). A full replace, not a patch: every field is written
/// as given, so omitted optional fields are cleared.
/// </summary>
/// <param name="Name">The member's own name — a first name or a full name. Required.</param>
/// <param name="City">A city name from the shared catalog; resolved to its canonical spelling and id.</param>
/// <param name="Nickname">Optional name strangers see before a match. Omitted means the first letter of <paramref name="Name"/>.</param>
public sealed record UpdateMemberProfileRequest(
    string Name,
    Gender Gender,
    DateOnly DateOfBirth,
    string City,
    string? Nickname = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? Work = null,
    string? Religion = null);
