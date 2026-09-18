using Aynera.Domain.Common;
using Aynera.Domain.EarlyAccess.Requests;
using Aynera.Domain.EarlyAccess.Responses;

namespace Aynera.Application.Features.EarlyAccess.Services.Interfaces;

public interface IEarlyAccessService
{
    Task<IReadOnlyList<EarlyAccessCityDto>> ListOpenCitiesAsync(CancellationToken cancellationToken);

    Task<PagedResult<EarlyAccessSignupAdminDto>> ListSignupsAsync(
        PagedQuery query,
        CancellationToken cancellationToken);

    Task<EarlyAccessSignupDto> RegisterAsync(
        JoinEarlyAccessRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);
}
