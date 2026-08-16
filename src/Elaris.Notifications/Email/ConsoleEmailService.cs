using Elaris.Application.Features.Auth.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Elaris.Notifications.Email;

/// <summary>
/// Dev stub for email. Does not send mail and never logs verify URLs or raw email addresses.
/// </summary>
public sealed class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken)
    {
        _ = email;
        _ = verifyUrl;
        _logger.LogInformation("Console email verification dispatched (address and link not logged).");
        return Task.CompletedTask;
    }

    public Task SendOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        _ = email;
        _ = code;
        _logger.LogInformation("Console email OTP dispatched (code and address not logged).");
        return Task.CompletedTask;
    }
}
