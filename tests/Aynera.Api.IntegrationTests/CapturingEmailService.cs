using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;

namespace Aynera.Api.IntegrationTests;

/// <summary>Test double that records the last verification email.</summary>
public sealed class CapturingEmailService : IEmailService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _verificationLinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VenueHeadsUpNotice> _venueHeadsUps = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastEmail;
    private string? _lastVerifyUrl;
    private string? _lastOtpCode;

    public string? LastEmail
    {
        get
        {
            lock (_gate)
            {
                return _lastEmail;
            }
        }
    }

    public string? LastVerifyUrl
    {
        get
        {
            lock (_gate)
            {
                return _lastVerifyUrl;
            }
        }
    }

    public Task SendVerificationLinkAsync(string email, string verifyUrl, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _lastEmail = email;
            _lastVerifyUrl = verifyUrl;
            _verificationLinks[email] = verifyUrl;
        }

        return Task.CompletedTask;
    }

    public string? GetVerificationLink(string email)
    {
        lock (_gate) return _verificationLinks.GetValueOrDefault(email);
    }

    public Task SendOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _lastEmail = email;
            _lastOtpCode = code;
        }

        return Task.CompletedTask;
    }

    public string? GetOtpCode(string email)
    {
        lock (_gate)
        {
            return string.Equals(_lastEmail, email, StringComparison.OrdinalIgnoreCase)
                ? _lastOtpCode
                : null;
        }
    }

    public Task SendVenueHeadsUpAsync(string email, VenueHeadsUpNotice notice, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _venueHeadsUps[email] = notice;
        }

        return Task.CompletedTask;
    }

    public VenueHeadsUpNotice? GetVenueHeadsUp(string email)
    {
        lock (_gate) return _venueHeadsUps.GetValueOrDefault(email);
    }
}
