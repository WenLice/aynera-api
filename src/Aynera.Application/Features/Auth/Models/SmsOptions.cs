namespace Aynera.Application.Features.Auth.Models;

public sealed class SmsOptions
{
    public const string SectionName = "Aynera:Sms";

    /// <summary>
    /// <c>Console</c> (no send), <c>TwoFactor</c> (2Factor.in, the Indian OTP route) or
    /// <c>Textbelt</c> (free-tier HTTP SMS; 1 free SMS/day with key <c>textbelt</c>).
    /// </summary>
    public string Provider { get; set; } = "Console";

    public string TextbeltApiUrl { get; set; } = "https://textbelt.com/text";

    /// <summary>Use <c>textbelt</c> for the public free quota, or a paid Textbelt key.</summary>
    public string TextbeltApiKey { get; set; } = "textbelt";

    /// <summary>API key from the 2Factor dashboard. Never commit it; set it per environment.</summary>
    public string TwoFactorApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Name of the DLT-approved template registered in the 2Factor panel. Indian carriers
    /// require one for A2P SMS, and the message a member receives is the template's text with
    /// the code substituted — so the wording lives there, not in this codebase. Left empty,
    /// <see cref="TwoFactorOtpUrl"/> drops the segment and 2Factor falls back to its own
    /// default template, which is enough to test with before a template is approved.
    /// </summary>
    public string TwoFactorTemplateName { get; set; } = string.Empty;

    /// <summary>
    /// The whole request URL, as a template. <c>{apiKey}</c>, <c>{phone}</c> (digits only, with
    /// country code), <c>{code}</c> and <c>{template}</c> are substituted; a trailing
    /// <c>/{template}</c> is dropped when no template name is configured.
    ///
    /// It is a template rather than a hard-coded URL so that an account whose route differs —
    /// a different path, or a number expected without its country code — is a configuration
    /// change instead of a code change.
    /// </summary>
    public string TwoFactorOtpUrl { get; set; } = "https://2factor.in/API/V1/{apiKey}/SMS/{phone}/{code}/{template}";
}
