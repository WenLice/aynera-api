using Aynera.Application.Features.Liveness.Models;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Domain.Liveness.Exceptions;

namespace Aynera.Infrastructure.Services;

/// <summary>
/// Used when <c>Aynera:Aws</c> is not configured outside tests. Says so plainly (503) instead of
/// pretending a check passed — a face check that cannot fail is worse than none.
/// </summary>
public sealed class UnavailableFaceLivenessProvider : IFaceLivenessProvider
{
    public string Region => throw Unavailable();
    public string IdentityPoolId => throw Unavailable();

    public Task<string> CreateSessionAsync(CancellationToken cancellationToken) => throw Unavailable();

    public Task<FaceLivenessResult> GetResultAsync(string sessionId, CancellationToken cancellationToken) =>
        throw Unavailable();

    private static LivenessException Unavailable() =>
        new("liveness_unavailable", "The face check is not set up yet.", statusCode: 503);
}
