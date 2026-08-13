using Elaris.Domain.EarlyAccess.Requests;
using Elaris.Domain.EarlyAccess.Responses;

namespace Elaris.Application.Features.EarlyAccess.Services.Interfaces;

public interface IEarlyAccessService
{
    Task<IReadOnlyList<EarlyAccessCityDto>> ListOpenCitiesAsync(CancellationToken cancellationToken);

    Task<EarlyAccessSignupDto> RegisterAsync(
        JoinEarlyAccessRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);
}
