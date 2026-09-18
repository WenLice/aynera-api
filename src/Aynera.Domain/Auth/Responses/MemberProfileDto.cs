namespace Aynera.Domain.Auth.Responses;

/// <summary>
/// The member's basic details. <paramref name="Name"/> is what the member wrote — a first name or a
/// full name — and is always present. <paramref name="Nickname"/> is null unless the member chose one.
/// <paramref name="CityId"/> is the shared city-catalog id (same key as venues); every member has exactly one.
/// </summary>
public sealed record MemberProfileDto(
    string Name,
    string Gender,
    DateOnly DateOfBirth,
    string City,
    Guid CityId,
    string? Nickname = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? Work = null,
    string? Religion = null);
