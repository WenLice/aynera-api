namespace Elaris.Application.Features.Auth.Services.Interfaces;

public interface IEmailService
{
    Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken);
}
