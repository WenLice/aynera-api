using Aynera.Application.Features.Venues.Models;

namespace Aynera.Application.Features.Auth.Services.Interfaces;

public interface ISmsService
{
    Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken);

    Task SendVenueHeadsUpAsync(string phoneE164, VenueHeadsUpNotice notice, CancellationToken cancellationToken);
}
