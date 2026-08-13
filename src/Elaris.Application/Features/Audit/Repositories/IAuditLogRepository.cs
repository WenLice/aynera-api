using Elaris.Domain.Audit.Records;

namespace Elaris.Application.Features.Audit.Repositories;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogRecord log, CancellationToken cancellationToken);
}
