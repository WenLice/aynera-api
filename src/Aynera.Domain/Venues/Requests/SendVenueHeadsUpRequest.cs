namespace Aynera.Domain.Venues.Requests;

/// <summary>
/// Admin-triggered heads-up to a venue's contact: a party of members plans to visit on a date.
/// Sent to the venue over email and SMS. No member identities are included.
/// </summary>
public sealed record SendVenueHeadsUpRequest(
    DateOnly VisitOn,
    int PartySize,
    string? Note = null);
