namespace Aynera.Domain.Venues.Requests;

public sealed record UpdateVenueRequest(
    string? Name = null,
    string? Type = null,
    Guid? CityId = null,
    string? Area = null,
    string? Address = null,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhoneE164 = null,
    IReadOnlyList<string>? PhotoUrls = null,
    int? Capacity = null,
    string? Notes = null,
    bool? IsActive = null);
