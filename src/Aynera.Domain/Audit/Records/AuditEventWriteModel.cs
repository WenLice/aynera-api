using Aynera.Domain.Audit.Statics;

namespace Aynera.Domain.Audit.Records;

public sealed record AuditLogRecord(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Level,
    string Message,
    string? Category,
    string Client,
    string? CorrelationId,
    Guid? UserId,
    string? ClientIp,
    string? PropertiesJson);

public sealed record AuditEventWriteModel(
    string Action,
    string Outcome,
    string Message,
    string? Category = null,
    string Level = AuditLogLevels.Information,
    string Client = AuditClients.Api,
    Guid? UserId = null,
    string? ClientIp = null,
    Guid? SubjectUserId = null,
    string? SubjectType = null,
    string? SubjectId = null,
    string? Audience = null,
    IReadOnlyDictionary<string, AuditFieldChange>? Changes = null,
    object? Metadata = null);
