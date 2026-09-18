using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Notifications.Email;

/// <summary>
/// Dev stub for email. Does not send mail and never logs verify URLs, codes or raw email addresses —
/// unless <see cref="OtpOptions.RevealCodesInLogs"/> is on, which the host only allows in Development.
/// </summary>
public sealed class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;
    private readonly IOptionsMonitor<OtpOptions> _otpOptions;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger, IOptionsMonitor<OtpOptions> otpOptions)
    {
        _logger = logger;
        _otpOptions = otpOptions;
    }

    public Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken)
    {
        if (_otpOptions.CurrentValue.RevealCodesInLogs)
        {
            _logger.LogWarning("DEV ONLY — verification link for {Email}: {Url}", email, verifyUrl);
        }
        else
        {
            _logger.LogInformation("Console email verification dispatched (address and link not logged).");
        }

        return Task.CompletedTask;
    }

    public Task SendOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        if (_otpOptions.CurrentValue.RevealCodesInLogs)
        {
            _logger.LogWarning("DEV ONLY — email OTP for {Email}: {Code}", email, code);
        }
        else
        {
            _logger.LogInformation("Console email OTP dispatched (code and address not logged).");
        }

        return Task.CompletedTask;
    }

    public Task SendVenueHeadsUpAsync(string email, VenueHeadsUpNotice notice, CancellationToken cancellationToken)
    {
        _ = email;
        _ = notice;
        _logger.LogInformation("Console email venue heads-up dispatched (address and details not logged).");
        return Task.CompletedTask;
    }
}
