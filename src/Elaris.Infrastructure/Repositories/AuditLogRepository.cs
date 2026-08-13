using Elaris.Application.Features.Audit.Repositories;
using Elaris.Domain.Audit.Records;
using Elaris.Persistence;
using Elaris.Persistence.Entities;

namespace Elaris.Infrastructure.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly ElarisDbContext _db;

    public AuditLogRepository(ElarisDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AuditLogRecord log, CancellationToken cancellationToken)
    {
        _db.AuditLogs.Add(new AuditLog
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
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
