namespace Aynera.Application.Features.Liveness.Services.Interfaces;

/// <summary>The member's face as proven live by their latest passed face check.</summary>
public sealed record VerifiedFace(byte[] Data, string ContentType);

/// <summary>
/// The identity anchor every photo and the intro video are matched against. It comes from the face
/// check — a frame AWS captured from a live person — rather than from an uploaded photo, which
/// could be anyone's.
/// </summary>
public interface IVerifiedFaceProvider
{
    /// <summary>Null until the member's latest finished face check passed.</summary>
    Task<VerifiedFace?> GetAsync(Guid userId, CancellationToken cancellationToken);
}
