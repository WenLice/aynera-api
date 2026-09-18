namespace Aynera.Domain.Audit.Responses;

public sealed record AuditEventAdminDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Action,
    string Outcome,
    string Message,
    Guid? ActorUserId,
    Guid? SubjectUserId,
    string? ChangesJson,
    string? CorrelationId);
