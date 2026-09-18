using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Notifications.Sms;

/// <summary>
/// Free-tier SMS via Textbelt (<c>key=textbelt</c> ≈ 1 free SMS/day). Never logs OTP or phone.
/// </summary>
public sealed class TextbeltSmsService : ISmsService
{
    private readonly HttpClient _http;
    private readonly SmsOptions _options;
    private readonly ILogger<TextbeltSmsService> _logger;

    public TextbeltSmsService(
        HttpClient http,
        IOptions<SmsOptions> options,
        ILogger<TextbeltSmsService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        await SendAsync(phoneE164, $"Your Aynera code is {code}. It expires soon. Do not share it.", cancellationToken);
        _logger.LogInformation("Textbelt SMS OTP dispatched (code and phone not logged).");
    }

    public async Task SendVenueHeadsUpAsync(
        string phoneE164,
        VenueHeadsUpNotice notice,
        CancellationToken cancellationToken)
    {
        var message =
            $"Aynera: ~{notice.PartySize} guest(s) plan to visit {notice.VenueName} on {notice.VisitOn:dd MMM}. "
            + "Please prepare; reserve a table/space if needed.";
        await SendAsync(phoneE164, message, cancellationToken);
        _logger.LogInformation("Textbelt SMS venue heads-up dispatched (phone and details not logged).");
    }

    private async Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["phone"] = phoneE164.Trim(),
            ["message"] = message,
            ["key"] = string.IsNullOrWhiteSpace(_options.TextbeltApiKey) ? "textbelt" : _options.TextbeltApiKey.Trim()
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(
            string.IsNullOrWhiteSpace(_options.TextbeltApiUrl)
                ? "https://textbelt.com/text"
                : _options.TextbeltApiUrl.Trim(),
            content,
            cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<TextbeltResponse>(cancellationToken);
        if (!response.IsSuccessStatusCode || payload is not { Success: true })
        {
            var error = string.IsNullOrWhiteSpace(payload?.Error) ? "SMS send failed." : payload.Error;
            _logger.LogWarning("Textbelt SMS dispatch failed.");
            throw new InvalidOperationException(error);
        }
    }

    private sealed class TextbeltResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
