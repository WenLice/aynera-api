using Elaris.Application.Features.Auth.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Elaris.Notifications.Sms;

/// <summary>
/// Dev stub for SMS. Does not send messages and never logs OTP codes or full phone numbers.
/// </summary>
public sealed class ConsoleSmsService : ISmsService
{
    private readonly ILogger<ConsoleSmsService> _logger;

    public ConsoleSmsService(ILogger<ConsoleSmsService> logger)
    {
        _logger = logger;
    }

    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        _ = phoneE164;
        _ = code;
        _logger.LogInformation("Console SMS OTP dispatched (code and phone not logged).");
        return Task.CompletedTask;
    }
}
