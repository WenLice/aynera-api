using Aynera.Application.Features.Liveness.Services.Interfaces;

namespace Aynera.Application.Tests;

/// <summary>A member who has (or has not) passed the face check.</summary>
internal sealed class FixedVerifiedFace(bool verified = true) : IVerifiedFaceProvider
{
    public static readonly FixedVerifiedFace Verified = new(true);
    public static readonly FixedVerifiedFace NotVerified = new(false);

    public Task<VerifiedFace?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(verified ? new VerifiedFace([9, 9, 9], "image/jpeg") : null);
}
