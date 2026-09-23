namespace Aynera.Domain.Liveness.Records;

/// <summary>One liveness session, tied to the member who started it.</summary>
/// <param name="SessionId">The provider's session id (AWS Rekognition Face Liveness).</param>
/// <param name="Confidence">The provider's liveness confidence, 0–100.</param>
/// <param name="Similarity">How closely the live face matched the reference photo, 0–100.</param>
public sealed record LivenessSessionRecord(
    string SessionId,
    Guid UserId,
    string Outcome,
    decimal? Confidence,
    decimal? Similarity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
