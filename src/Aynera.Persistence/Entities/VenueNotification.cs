using Aynera.Domain.Venues.Enums;

namespace Aynera.Persistence.Entities;

/// <summary>
/// Durable outbox row for a venue heads-up. The recipient address/phone is resolved from the
/// <see cref="Venue"/> at delivery time; only the payload (visit date, party size, note) is stored here.
/// </summary>
public sealed class VenueNotification
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public VenueNotificationChannel Channel { get; set; }
    public DateOnly VisitOn { get; set; }
    public int PartySize { get; set; }
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public DateTimeOffset? AbandonedAtUtc { get; set; }

    /// <summary>Error type name only — never a provider message, recipient, or PII.</summary>
    public string? LastError { get; set; }
}
