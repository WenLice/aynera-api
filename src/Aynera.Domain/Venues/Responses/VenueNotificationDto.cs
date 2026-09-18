namespace Aynera.Domain.Venues.Responses;

public sealed record VenueNotificationDto(
    Guid Id,
    Guid VenueId,
    string Channel,
    DateOnly VisitOn,
    int PartySize,
    string? Note,
    string Status,
    int Attempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? AbandonedAtUtc);
