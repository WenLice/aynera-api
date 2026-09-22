using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Notifications.Sms;

/// <summary>
/// SMS through 2Factor.in, an Indian OTP route. Chosen over an international sender because
/// Indian carriers filter A2P traffic that is not registered under DLT, so a code sent from
/// abroad may simply never arrive.
///
/// <para>This deliberately sends <b>our</b> code rather than using 2Factor's AUTOGEN/VERIFY
/// pair. The OTP lifecycle — purpose binding, TTL, attempt counting and the atomic consume that
/// stops two instances spending the same code — lives in <c>IOtpChallengeRepository</c> and is
/// tested there. Letting the provider hold the code would quietly bypass all of it.</para>
///
/// <para>Never logs the code or the number.</para>
/// </summary>
public sealed class TwoFactorSmsService : ISmsService
{
    private readonly HttpClient _http;
    private readonly SmsOptions _options;
    private readonly ILogger<TwoFactorSmsService> _logger;

    public TwoFactorSmsService(
        HttpClient http,
        IOptions<SmsOptions> options,
        ILogger<TwoFactorSmsService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TwoFactorApiKey))
        {
            throw new InvalidOperationException(
                "2Factor is the configured SMS provider but Aynera:Sms:TwoFactorApiKey is empty.");
        }

        var url = BuildUrl(phoneE164, code);

        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await ReadAsync(response, cancellationToken);

        // 2Factor answers 200 with {"Status":"Success"|"Error", ...}, so the status code alone
        // does not tell us whether the message was accepted.
        if (!response.IsSuccessStatusCode
            || payload is null
            || !string.Equals(payload.Status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("2Factor SMS dispatch failed: {Status}", payload?.Status ?? "no response body");
            throw new InvalidOperationException(payload?.Details ?? "SMS send failed.");
        }

        _logger.LogInformation("2Factor SMS OTP dispatched (code and phone not logged).");
    }

    /// <summary>
    /// Not available on the OTP route: it carries a code into a fixed template, not free text.
    /// A venue heads-up is an ordinary transactional message and needs its own DLT template on a
    /// transactional route, so this fails loudly rather than dropping a notification silently —
    /// the outbox records the failed attempt and retries, exactly as it does for any other
    /// provider error. Leave venue notifications on another provider until that route exists.
    /// </summary>
    public Task SendVenueHeadsUpAsync(
        string phoneE164,
        VenueHeadsUpNotice notice,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "2Factor is configured for OTP only. Venue heads-up messages need a transactional "
            + "route with its own DLT template; configure a provider that has one.");

    private string BuildUrl(string phoneE164, string code)
    {
        var template = _options.TwoFactorTemplateName.Trim();
        var url = _options.TwoFactorOtpUrl.Trim();

        // With no approved template yet, drop the segment so 2Factor uses its own default.
        if (template.Length == 0)
        {
            url = url.Replace("/{template}", string.Empty, StringComparison.Ordinal);
        }

        return url
            .Replace("{apiKey}", Uri.EscapeDataString(_options.TwoFactorApiKey.Trim()), StringComparison.Ordinal)
            .Replace("{phone}", Uri.EscapeDataString(DigitsOnly(phoneE164)), StringComparison.Ordinal)
            .Replace("{code}", Uri.EscapeDataString(code), StringComparison.Ordinal)
            .Replace("{template}", Uri.EscapeDataString(template), StringComparison.Ordinal);
    }

    /// <summary>E.164 carries a leading <c>+</c>; the route wants bare digits.</summary>
    private static string DigitsOnly(string phone) =>
        new(phone.Where(char.IsAsciiDigit).ToArray());

    private static async Task<TwoFactorResponse?> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TwoFactorResponse>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // An error page instead of JSON is itself a failure; report it as one.
            return null;
        }
    }

    private sealed class TwoFactorResponse
    {
        [JsonPropertyName("Status")]
        public string? Status { get; set; }

        /// <summary>A session id on success, or the reason on failure.</summary>
        [JsonPropertyName("Details")]
        public string? Details { get; set; }
    }
}
