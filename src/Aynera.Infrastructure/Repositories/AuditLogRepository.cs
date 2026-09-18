using Aynera.Application.Features.Audit.Repositories;
using Aynera.Domain.Audit.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;

namespace Aynera.Infrastructure.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly AyneraDbContext _db;

    public AuditLogRepository(AyneraDbContext db)
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
