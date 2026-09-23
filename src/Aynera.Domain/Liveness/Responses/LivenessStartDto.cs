namespace Aynera.Domain.Liveness.Responses;

/// <summary>
/// What the app needs to run a check: open <paramref name="PageUrl"/> (in a WebView on phones, an
/// iframe on the web), then call Complete with <paramref name="SessionId"/> when the page says done.
/// </summary>
public sealed record LivenessStartDto(string SessionId, string Region, string PageUrl);
