using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;

namespace Aynera.Api.IntegrationTests;

/// <summary>Captures the last OTP per phone for automated tests.</summary>
public sealed class CapturingSmsService : ISmsService
{
    private readonly Dictionary<string, string> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VenueHeadsUpNotice> _venueHeadsUps = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Codes => _codes;

    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        _codes[phoneE164] = code;
        return Task.CompletedTask;
    }

    public string? GetCode(string phoneE164) =>
        _codes.TryGetValue(phoneE164, out var code) ? code : null;

    public Task SendVenueHeadsUpAsync(string phoneE164, VenueHeadsUpNotice notice, CancellationToken cancellationToken)
    {
        _venueHeadsUps[phoneE164] = notice;
        return Task.CompletedTask;
    }

    public VenueHeadsUpNotice? GetVenueHeadsUp(string phoneE164) =>
        _venueHeadsUps.GetValueOrDefault(phoneE164);
}
