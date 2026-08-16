namespace Elaris.Application.Features.Auth.Services.Interfaces;

public interface IEmailService
{
    Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken);

    Task SendOtpAsync(string email, string code, CancellationToken cancellationToken);
}
