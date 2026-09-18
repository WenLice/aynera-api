using Aynera.Application.Features.Venues.Models;

namespace Aynera.Application.Features.Auth.Services.Interfaces;

public interface IEmailService
{
    Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken);

    Task SendOtpAsync(string email, string code, CancellationToken cancellationToken);

    Task SendVenueHeadsUpAsync(string email, VenueHeadsUpNotice notice, CancellationToken cancellationToken);
}
