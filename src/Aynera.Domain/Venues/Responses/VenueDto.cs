namespace Aynera.Domain.Venues.Responses;

/// <summary>
/// Admin-facing venue projection. Includes full address and venue-contact details,
/// which are never exposed to members. The slim member "standard venue" projection
/// (name + area only) is a separate future response.
/// </summary>
public sealed record VenueDto(
    Guid Id,
    string Name,
    string Type,
    Guid CityId,
    string? CityName,
    string Area,
    string Address,
    IReadOnlyList<string> PhotoUrls,
    string ContactName,
    string ContactEmail,
    string ContactPhoneE164,
    int? Capacity,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
