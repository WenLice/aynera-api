using Aynera.Domain.Venues.Enums;

namespace Aynera.Domain.Venues.Records;

public sealed record VenueRecord(
    Guid Id,
    string Name,
    VenueType Type,
    Guid CityId,
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
