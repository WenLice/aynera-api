namespace Elaris.Domain.Media.Responses;

public sealed record MediaAuthenticityResult(
    bool IsLikelyAuthentic,
    string? Detail = null,
    string? Detector = null);
