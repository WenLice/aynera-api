using Aynera.Application.Features.Liveness.Models;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Aynera.Infrastructure.Services;

/// <summary>
/// Tests only: every session succeeds with high confidence and a plain frame, so the rest of the
/// flow — ownership, thresholds, face match, the selfie, registration progress — can be exercised.
/// </summary>
public sealed class StubFaceLivenessProvider : IFaceLivenessProvider
{
    public string Region => "ap-south-1";
    public string IdentityPoolId => "ap-south-1:00000000-0000-0000-0000-000000000000";

    public Task<string> CreateSessionAsync(CancellationToken cancellationToken) =>
        Task.FromResult("stub-" + Guid.NewGuid().ToString("N"));

    public Task<FaceLivenessResult> GetResultAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(48, 48, new Rgb24(200, 160, 140));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return Task.FromResult(new FaceLivenessResult("SUCCEEDED", 99.5m, stream.ToArray()));
    }
}
