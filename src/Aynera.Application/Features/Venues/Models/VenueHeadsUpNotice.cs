namespace Aynera.Application.Features.Venues.Models;

/// <summary>
/// Content resolved at delivery time for a venue heads-up. Carries no member identities.
/// </summary>
public sealed record VenueHeadsUpNotice(
    string VenueName,
    string Area,
    DateOnly VisitOn,
    int PartySize,
    string? Note);
