using Aynera.Application.Features.Liveness.Models;

namespace Aynera.Application.Features.Liveness.Services.Interfaces;

/// <summary>
/// The liveness vendor (AWS Rekognition Face Liveness). The video goes from the member's camera
/// straight to the vendor; this side only opens a session and reads its result.
/// </summary>
public interface IFaceLivenessProvider
{
    /// <summary>The region the page must stream to — it has to match the session's.</summary>
    string Region { get; }

    /// <summary>The Cognito identity pool the page uses to stream. Not a secret.</summary>
    string IdentityPoolId { get; }

    Task<string> CreateSessionAsync(CancellationToken cancellationToken);

    Task<FaceLivenessResult> GetResultAsync(string sessionId, CancellationToken cancellationToken);
}
