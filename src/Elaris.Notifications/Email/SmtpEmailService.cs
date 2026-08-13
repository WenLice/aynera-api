using Elaris.Application.Features.Auth.Models;
using Elaris.Application.Features.Auth.Services.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Elaris.Notifications.Email;

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
        message.Subject = "Confirm your ElAris email";
        message.Body = new TextPart("plain")
        {
            Text = string.Join(
                Environment.NewLine,
                "Confirm your ElAris email by opening this link:",
                verifyUrl,
                string.Empty,
                "If you did not create an account, you can ignore this message.")
        };

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

        _logger.LogInformation("SMTP email verification dispatched (address and link not logged).");
    }
}
