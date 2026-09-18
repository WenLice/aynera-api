using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Aynera.Notifications.Email;

/// <summary>Sends verification email over SMTP. Never logs address or verify URL.</summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendVerificationLinkAsync(
        string email,
        string verifyUrl,
        CancellationToken cancellationToken)
    {
        await SendPlainTextAsync(
            email,
            "Confirm your Aynera email",
            string.Join(
                Environment.NewLine,
                "Confirm your Aynera email by opening this link:",
                verifyUrl,
                string.Empty,
                "If you did not create an account, you can ignore this message."),
            cancellationToken);

        _logger.LogInformation("SMTP email verification dispatched (address and link not logged).");
    }

    public async Task SendOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        await SendPlainTextAsync(
            email,
            "Your Aynera verification code",
            string.Join(
                Environment.NewLine,
                "Your Aynera verification code:",
                code,
                string.Empty,
                "If you did not request this, you can ignore this message."),
            cancellationToken);

        _logger.LogInformation("SMTP email OTP dispatched (code and address not logged).");
    }

    public async Task SendVenueHeadsUpAsync(
        string email,
        VenueHeadsUpNotice notice,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            $"Hello {notice.VenueName} team,",
            string.Empty,
            $"A group from Aynera plans to visit on {notice.VisitOn:dd MMM yyyy}.",
            $"Location: {notice.Area}",
            $"Approximate party size: {notice.PartySize}."
        };

        if (!string.IsNullOrWhiteSpace(notice.Note))
        {
            lines.Add($"Note: {notice.Note}");
        }

        lines.Add(string.Empty);
        lines.Add("You may choose to reserve a table or space. No action is required in Aynera.");
        lines.Add("— Aynera");

        await SendPlainTextAsync(
            email,
            $"Aynera guests visiting {notice.VenueName} on {notice.VisitOn:dd MMM}",
            string.Join(Environment.NewLine, lines),
            cancellationToken);

        _logger.LogInformation("SMTP email venue heads-up dispatched (address and details not logged).");
    }

    private async Task SendPlainTextAsync(
        string email,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            throw new InvalidOperationException("SMTP host is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException("SMTP from address is not configured.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromDisplayName, _options.FromAddress.Trim()));
        message.To.Add(MailboxAddress.Parse(email.Trim()));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        var secure =
            _options.SmtpUseSsl
                ? (_options.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None;

        await client.ConnectAsync(_options.SmtpHost.Trim(), _options.SmtpPort, secure, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
        {
            await client.AuthenticateAsync(
                _options.SmtpUsername.Trim(),
                _options.SmtpPassword ?? string.Empty,
                cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
