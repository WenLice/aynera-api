namespace Elaris.Domain.Photos.Responses;

public sealed record FaceMatchResult(
    string Status,
    decimal? Score,
    string? Detail = null);
