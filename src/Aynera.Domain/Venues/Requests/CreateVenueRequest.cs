namespace Aynera.Domain.Venues.Requests;

public sealed record CreateVenueRequest(
    string Name,
    string Type,
    Guid CityId,
    string Area,
    string Address,
    string ContactName,
    string ContactEmail,
    string ContactPhoneE164,
    IReadOnlyList<string>? PhotoUrls = null,
    int? Capacity = null,
    string? Notes = null,
    bool IsActive = true);
