namespace Aynera.Persistence.Entities;

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid AuditLogId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public Guid? SubjectUserId { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectId { get; set; }
    public string? Audience { get; set; }
    public string? Changes { get; set; }
    public string? MetadataJson { get; set; }

    public AuditLog AuditLog { get; set; } = null!;
}
