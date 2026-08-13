using Elaris.Domain.EarlyAccess.Requests;
using Elaris.Domain.EarlyAccess.Responses;

namespace Elaris.Application.Features.EarlyAccess.Services.Interfaces;

public interface IEarlyAccessCityService
{
    Task<IReadOnlyList<EarlyAccessCityDto>> ListAsync(CancellationToken cancellationToken);

    Task<EarlyAccessCityDto> CreateAsync(CreateEarlyAccessCityRequest request, CancellationToken cancellationToken);

    Task<EarlyAccessCityDto> UpdateAsync(
        Guid id,
        UpdateEarlyAccessCityRequest request,
        CancellationToken cancellationToken);

    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);
}
