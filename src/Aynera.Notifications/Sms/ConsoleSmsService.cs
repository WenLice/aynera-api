using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Notifications.Sms;

/// <summary>
/// Dev stub for SMS. Does not send messages and never logs OTP codes or full phone numbers —
/// unless <see cref="OtpOptions.RevealCodesInLogs"/> is on, which the host only allows in Development.
/// </summary>
public sealed class ConsoleSmsService : ISmsService
{
    private readonly ILogger<ConsoleSmsService> _logger;
    private readonly IOptionsMonitor<OtpOptions> _otpOptions;

    public ConsoleSmsService(ILogger<ConsoleSmsService> logger, IOptionsMonitor<OtpOptions> otpOptions)
    {
        _logger = logger;
        _otpOptions = otpOptions;
    }

    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        if (_otpOptions.CurrentValue.RevealCodesInLogs)
        {
            _logger.LogWarning("DEV ONLY — SMS OTP for {Phone}: {Code}", phoneE164, code);
        }
        else
        {
            _logger.LogInformation("Console SMS OTP dispatched (code and phone not logged).");
        }

        return Task.CompletedTask;
    }

    public Task SendVenueHeadsUpAsync(string phoneE164, VenueHeadsUpNotice notice, CancellationToken cancellationToken)
    {
        _ = phoneE164;
        _ = notice;
        _logger.LogInformation("Console SMS venue heads-up dispatched (phone and details not logged).");
        return Task.CompletedTask;
    }
}
