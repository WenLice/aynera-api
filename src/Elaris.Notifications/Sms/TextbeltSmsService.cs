using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Notifications.Sms;

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
        var phone = phoneE164.Trim();
        var form = new Dictionary<string, string>
        {
            ["phone"] = phone,
            ["message"] = $"Your ElAris code is {code}. It expires soon. Do not share it.",
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

        _logger.LogInformation("Textbelt SMS OTP dispatched (code and phone not logged).");
    }

    private sealed class TextbeltResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
