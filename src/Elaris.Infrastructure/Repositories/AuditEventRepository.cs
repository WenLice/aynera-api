using Elaris.Application.Features.Audit.Repositories;
using Elaris.Domain.Audit.Records;
using Elaris.Domain.Audit.Statics;
using Elaris.Persistence;
using Elaris.Persistence.Entities;

namespace Elaris.Infrastructure.Repositories;

public sealed class AuditEventRepository : IAuditEventRepository
{
    private readonly ElarisDbContext _db;

    public AuditEventRepository(ElarisDbContext db)
    {
        _db = db;
    }

    public async Task AddWithLogAsync(
        AuditLogRecord log,
        string action,
        string outcome,
        Guid? subjectUserId,
        string? subjectType,
        string? subjectId,
        string? audience,
        IReadOnlyDictionary<string, AuditFieldChange>? changes,
        object? metadata,
        CancellationToken cancellationToken)
    {
        var logEntity = new AuditLog
        {
            Id = log.Id,
            OccurredAtUtc = log.OccurredAtUtc,
            Level = log.Level,
            Message = log.Message,
            Category = log.Category,
            Client = log.Client,
            CorrelationId = log.CorrelationId,
            UserId = log.UserId,
            ClientIp = log.ClientIp,
            PropertiesJson = log.PropertiesJson
        };

        var eventEntity = new AuditEvent
        {
            Id = Guid.NewGuid(),
            AuditLogId = log.Id,
            Action = action,
            Outcome = outcome,
            SubjectUserId = subjectUserId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Audience = audience,
            Changes = AuditChanges.ToJson(changes),
            MetadataJson = AuditChanges.MetadataToJson(metadata),
            AuditLog = logEntity
        };

        _db.AuditLogs.Add(logEntity);
        _db.AuditEvents.Add(eventEntity);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
