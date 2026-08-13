using Elaris.Application.Features.Auth.Services.Interfaces;

namespace Elaris.Api.IntegrationTests;

/// <summary>Captures the last OTP per phone for automated tests.</summary>
public sealed class CapturingSmsService : ISmsService
{
    private readonly Dictionary<string, string> _codes = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Codes => _codes;

    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        _codes[phoneE164] = code;
        return Task.CompletedTask;
    }

    public string? GetCode(string phoneE164) =>
        _codes.TryGetValue(phoneE164, out var code) ? code : null;
}
