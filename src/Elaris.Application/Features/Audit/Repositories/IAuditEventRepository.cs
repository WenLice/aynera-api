using Elaris.Domain.Audit.Records;
using Elaris.Domain.Audit.Statics;

namespace Elaris.Application.Features.Audit.Repositories;

public interface IAuditEventRepository
{
    Task AddWithLogAsync(
        AuditLogRecord log,
        string action,
        string outcome,
        Guid? subjectUserId,
        string? subjectType,
        string? subjectId,
        string? audience,
        IReadOnlyDictionary<string, AuditFieldChange>? changes,
        object? metadata,
        CancellationToken cancellationToken);
}
