using Aynera.Application.Features.Audit.Repositories;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aynera.Infrastructure.Repositories;

public sealed class AuditEventRepository : IAuditEventRepository
{
    private readonly AyneraDbContext _db;

    public AuditEventRepository(AyneraDbContext db)
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

    public async Task<(IReadOnlyList<AuditEventAdminRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        IReadOnlyList<string>? actions,
        Guid? subjectUserId,
        string? subjectType,
        string? subjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = _db.AuditEvents.AsNoTracking().Include(e => e.AuditLog).AsQueryable();

        if (actions is { Count: > 0 })
        {
            query = query.Where(e => actions.Contains(e.Action));
        }

        if (subjectUserId is not null)
        {
            query = query.Where(e => e.SubjectUserId == subjectUserId);
        }

        if (!string.IsNullOrWhiteSpace(subjectType))
        {
            query = query.Where(e => e.SubjectType == subjectType);
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(e => e.SubjectId == subjectId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(e => e.AuditLog.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new AuditEventAdminRecord(
                e.Id,
                e.AuditLog.OccurredAtUtc,
                e.Action,
                e.Outcome,
                e.AuditLog.Message,
                e.AuditLog.UserId,
                e.SubjectUserId,
                e.Changes,
                e.AuditLog.CorrelationId))
            .ToListAsync(cancellationToken);

        return (rows, totalCount);
    }
}
