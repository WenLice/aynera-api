using Aynera.Domain.Audit.Records;

namespace Aynera.Application.Features.Audit.Repositories;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogRecord log, CancellationToken cancellationToken);
}
