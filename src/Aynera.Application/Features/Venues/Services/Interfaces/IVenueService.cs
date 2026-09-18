using Aynera.Domain.Venues.Requests;
using Aynera.Domain.Venues.Responses;

namespace Aynera.Application.Features.Venues.Services.Interfaces;

public interface IVenueService
{
    Task<IReadOnlyList<VenueDto>> ListAsync(CancellationToken cancellationToken);

    Task<VenueDto> CreateAsync(CreateVenueRequest request, CancellationToken cancellationToken);

    Task<VenueDto> UpdateAsync(Guid id, UpdateVenueRequest request, CancellationToken cancellationToken);

    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<VenueNotificationDto>> QueueHeadsUpAsync(
        Guid venueId,
        SendVenueHeadsUpRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VenueNotificationDto>> ListNotificationsAsync(
        Guid venueId,
        CancellationToken cancellationToken);
}
