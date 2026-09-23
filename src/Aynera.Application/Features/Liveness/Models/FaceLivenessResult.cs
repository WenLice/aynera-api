namespace Aynera.Application.Features.Liveness.Models;

/// <summary>What the provider reports for one session.</summary>
/// <param name="Status">CREATED, IN_PROGRESS, SUCCEEDED, FAILED or EXPIRED.</param>
/// <param name="Confidence">0–100; present once the session has succeeded.</param>
/// <param name="ReferenceImage">A clear frame of the face, JPEG; present once the session has succeeded.</param>
public sealed record FaceLivenessResult(string Status, decimal? Confidence, byte[]? ReferenceImage);
