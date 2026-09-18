namespace Aynera.Persistence.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Level { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Client { get; set; } = "api";
    public string? CorrelationId { get; set; }
    public Guid? UserId { get; set; }
    public string? ClientIp { get; set; }
    public string? PropertiesJson { get; set; }

    public AuditEvent? AuditEvent { get; set; }
}
