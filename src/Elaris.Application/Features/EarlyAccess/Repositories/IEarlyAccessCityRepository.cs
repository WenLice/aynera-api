using Elaris.Domain.EarlyAccess.Records;

namespace Elaris.Application.Features.EarlyAccess.Repositories;

public interface IEarlyAccessCityRepository
{
    Task<IReadOnlyList<EarlyAccessCityRecord>> ListOpenAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EarlyAccessCityRecord>> ListAllAsync(CancellationToken cancellationToken);

    Task<EarlyAccessCityRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<EarlyAccessCityRecord?> FindOpenByNameAsync(string name, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken);

    Task<EarlyAccessCityRecord> AddAsync(EarlyAccessCityRecord city, CancellationToken cancellationToken);

    Task<EarlyAccessCityRecord> UpdateAsync(EarlyAccessCityRecord city, CancellationToken cancellationToken);

    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);
}
