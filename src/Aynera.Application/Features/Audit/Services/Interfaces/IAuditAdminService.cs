using Aynera.Domain.Audit.Requests;
using Aynera.Domain.Audit.Responses;
using Aynera.Domain.Common;

namespace Aynera.Application.Features.Audit.Services.Interfaces;

public interface IAuditAdminService
{
    Task<PagedResult<AuditEventAdminDto>> ListAsync(
        Guid actorId,
        AuditAdminListQuery query,
        CancellationToken cancellationToken);
}
