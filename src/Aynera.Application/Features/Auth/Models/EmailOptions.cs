namespace Aynera.Application.Features.Auth.Models;

public sealed class EmailOptions
{
    public const string SectionName = "Aynera:Email";

    /// <summary>
    /// <c>Console</c> (no send), <c>Smtp</c> (requires host + from address) or <c>ZeptoMail</c>
    /// (HTTPS API; requires a token + from address — use it where outbound SMTP is blocked).
    /// </summary>
    public string Provider { get; set; } = "Console";

    /// <summary>ZeptoMail send endpoint for the account's data centre (India: <c>api.zeptomail.in</c>).</summary>
    public string ZeptoMailApiUrl { get; set; } = "https://api.zeptomail.in/v1.1/email";

    /// <summary>
    /// ZeptoMail send-mail token. When empty, <see cref="SmtpPassword"/> is used — it is the same
    /// token, so moving from SMTP to the API needs only the provider switch.
    /// </summary>
    public string? ZeptoMailToken { get; set; }

    /// <summary>
    /// Base URL for the email verification link (API or member web).
    /// Query string <c>userId</c> and <c>token</c> are appended.
    /// Member-web (or another landing page) should read those params and POST them to <c>/auth/verifyemail</c>.
    /// </summary>
    public string VerifyLinkBaseUrl { get; set; } = "http://localhost:5173/verify-email";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool SmtpUseSsl { get; set; } = true;

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }

    public string? FromAddress { get; set; }

    public string FromDisplayName { get; set; } = "Aynera";
}
