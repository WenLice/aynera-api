using Elaris.Domain.Audit.Requests;
using Elaris.Domain.Audit.Responses;
using Elaris.Domain.Common;

namespace Elaris.Application.Features.Audit.Services.Interfaces;

public interface IAuditAdminService
{
    Task<PagedResult<AuditEventAdminDto>> ListAsync(
        Guid actorId,
        AuditAdminListQuery query,
        CancellationToken cancellationToken);
}
