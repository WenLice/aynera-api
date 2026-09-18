namespace Aynera.Application.Features.Auth.Models;

public sealed class EmailOptions
{
    public const string SectionName = "Aynera:Email";

    /// <summary>
    /// <c>Console</c> (no send) or <c>Smtp</c> (requires host + from address).
    /// </summary>
    public string Provider { get; set; } = "Console";

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
