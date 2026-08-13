namespace Elaris.Application.Features.Auth.Models;

public sealed class SmsOptions
{
    public const string SectionName = "Elaris:Sms";

    /// <summary>
    /// <c>Console</c> (no send) or <c>Textbelt</c> (free-tier HTTP SMS; 1 free SMS/day with key <c>textbelt</c>).
    /// </summary>
    public string Provider { get; set; } = "Console";

    public string TextbeltApiUrl { get; set; } = "https://textbelt.com/text";

    /// <summary>Use <c>textbelt</c> for the public free quota, or a paid Textbelt key.</summary>
    public string TextbeltApiKey { get; set; } = "textbelt";
}
