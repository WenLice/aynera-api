using Aynera.Application.Features.Auth.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Aynera.Notifications.Email;

/// <summary>
/// Sends email over SMTP. Note that Render's free web services block outbound SMTP ports
/// (25/465/587), so there the connect simply hangs — use <see cref="ZeptoMailEmailService"/>
/// on that host.
/// </summary>
public sealed class SmtpEmailService : PlainTextEmailService
{
    private readonly EmailOptions _options;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
        : base(logger)
    {
        _options = options.Value;
    }

    protected override string TransportName => "SMTP";

    protected override async Task SendPlainTextAsync(
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
        // TLS Wrapper listens on 465 and 2465 and expects TLS from the first byte; every other
        // encrypted port negotiates with STARTTLS. Picking the wrong one does not degrade — the
        // connection simply hangs or is refused.
        var secure =
            _options.SmtpUseSsl
                ? (_options.SmtpPort is 465 or 2465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls)
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
