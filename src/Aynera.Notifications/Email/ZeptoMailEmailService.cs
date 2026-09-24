using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aynera.Application.Features.Auth.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Notifications.Email;

/// <summary>
/// Sends email through ZeptoMail's HTTPS API instead of SMTP. Exists because Render's free
/// web services block outbound SMTP ports (25/465/587): the SMTP connect hangs until the
/// request is cancelled, while HTTPS on 443 is allowed.
///
/// <para>The token is the Mail Agent's send-mail token — the same value used as the SMTP
/// password — so switching between the two transports is one configuration value.</para>
/// </summary>
public sealed class ZeptoMailEmailService : PlainTextEmailService
{
    private readonly HttpClient _http;
    private readonly EmailOptions _options;
    private readonly ILogger<ZeptoMailEmailService> _logger;

    public ZeptoMailEmailService(
        HttpClient http,
        IOptions<EmailOptions> options,
        ILogger<ZeptoMailEmailService> logger)
        : base(logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    protected override string TransportName => "ZeptoMail";

    protected override async Task SendPlainTextAsync(
        string email,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var token = string.IsNullOrWhiteSpace(_options.ZeptoMailToken)
            ? _options.SmtpPassword
            : _options.ZeptoMailToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "ZeptoMail is the configured email provider but no send-mail token is set.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException("Email from address is not configured.");
        }

        var payload = new SendRequest(
            new Address(_options.FromAddress.Trim(), _options.FromDisplayName),
            [new Recipient(new Address(email.Trim(), null))],
            subject,
            body);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.ZeptoMailApiUrl)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Authorization", $"Zoho-enczapikey {token.Trim()}");

        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The error body carries ZeptoMail's code and reason, never the recipient.
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "ZeptoMail send failed: {StatusCode} {Detail}",
                (int)response.StatusCode,
                detail.Length > 500 ? detail[..500] : detail);
            throw new InvalidOperationException($"Email send failed ({(int)response.StatusCode}).");
        }
    }

    private sealed record SendRequest(
        [property: JsonPropertyName("from")] Address From,
        [property: JsonPropertyName("to")] Recipient[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("textbody")] string TextBody);

    private sealed record Recipient(
        [property: JsonPropertyName("email_address")] Address EmailAddress);

    private sealed record Address(
        [property: JsonPropertyName("address")] string Value,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Name);
}
