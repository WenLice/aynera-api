using Aynera.Domain.Venues.Enums;

namespace Aynera.Domain.Venues.Records;

public sealed record VenueNotificationRecord(
    Guid Id,
    Guid VenueId,
    VenueNotificationChannel Channel,
    DateOnly VisitOn,
    int PartySize,
    string? Note,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    int Attempts,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? AbandonedAtUtc);
