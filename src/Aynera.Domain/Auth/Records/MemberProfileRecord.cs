namespace Aynera.Domain.Auth.Records;

/// <summary>
/// Application-facing member profile projection — the member's basic details.
/// <paramref name="Name"/> is the member's own name (first or full, their choice) and is required;
/// <paramref name="Nickname"/> is the optional name strangers see before a match.
/// <paramref name="CityId"/> is the shared city-catalog key (required).
/// </summary>
public sealed record MemberProfileRecord(
    Guid UserId,
    string Name,
    string Gender,
    DateOnly DateOfBirth,
    string City,
    Guid CityId,
    string? Nickname = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? Work = null,
    string? Religion = null,
    bool GenderIsPublic = true);
