namespace Elaris.Application.Features.Auth.Services.Interfaces;

public interface ISmsService
{
    Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken);
}
