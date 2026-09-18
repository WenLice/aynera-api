using Aynera.Domain.Venues.Records;

namespace Aynera.Application.Features.Venues.Repositories;

public interface IVenueRepository
{
    Task<IReadOnlyList<VenueRecord>> ListAllAsync(CancellationToken cancellationToken);

    Task<VenueRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<VenueRecord> AddAsync(VenueRecord venue, CancellationToken cancellationToken);

    Task<VenueRecord> UpdateAsync(VenueRecord venue, CancellationToken cancellationToken);

    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);
}
