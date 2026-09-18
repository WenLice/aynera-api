namespace Aynera.Domain.Audit.Records;

public sealed record AuditEventAdminRecord(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Action,
    string Outcome,
    string Message,
    Guid? ActorUserId,
    Guid? SubjectUserId,
    string? ChangesJson,
    string? CorrelationId);
