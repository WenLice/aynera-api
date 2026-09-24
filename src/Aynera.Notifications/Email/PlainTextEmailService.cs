using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using Microsoft.Extensions.Logging;

namespace Aynera.Notifications.Email;

/// <summary>
/// The wording of every email Aynera sends, shared by each transport so switching provider
/// never changes what a member reads. A transport only has to deliver one plain-text message.
/// Never logs the address, the code or the link.
/// </summary>
public abstract class PlainTextEmailService : IEmailService
{
    private readonly ILogger _logger;

    protected PlainTextEmailService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Shown in the dispatch log line, e.g. "SMTP" or "ZeptoMail".</summary>
    protected abstract string TransportName { get; }

    protected abstract Task SendPlainTextAsync(
        string email,
        string subject,
        string body,
        CancellationToken cancellationToken);

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

        _logger.LogInformation(
            "{Transport} email verification dispatched (address and link not logged).", TransportName);
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

        _logger.LogInformation(
            "{Transport} email OTP dispatched (code and address not logged).", TransportName);
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

        _logger.LogInformation(
            "{Transport} email venue heads-up dispatched (address and details not logged).", TransportName);
    }
}
