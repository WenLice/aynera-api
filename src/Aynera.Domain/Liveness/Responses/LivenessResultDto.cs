namespace Aynera.Domain.Liveness.Responses;

/// <summary>
/// The server's verdict — read from the provider by the server, never reported by the page.
/// </summary>
/// <param name="Outcome">Pending, Passed, NotLive, FaceMismatch, Expired or Failed.</param>
public sealed record LivenessResultDto(
    string SessionId,
    string Outcome,
    bool Passed,
    decimal? Confidence,
    decimal? Similarity,
    DateTimeOffset? CheckedAtUtc);
