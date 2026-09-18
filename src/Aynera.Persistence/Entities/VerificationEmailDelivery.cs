namespace Aynera.Persistence.Entities;

// Only identity is queued. Tokens and recipient addresses are resolved at delivery time.
public sealed class VerificationEmailDelivery
{
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
}
