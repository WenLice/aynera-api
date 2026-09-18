using Aynera.Domain.Venues.Records;

namespace Aynera.Application.Features.Venues.Repositories;

public interface IVenueNotificationRepository
{
    Task AddRangeAsync(IEnumerable<VenueNotificationRecord> notifications, CancellationToken cancellationToken);

    Task<IReadOnlyList<VenueNotificationRecord>> ListByVenueAsync(Guid venueId, CancellationToken cancellationToken);
}
