using Aynera.Domain.Liveness.Responses;

namespace Aynera.Application.Features.Liveness.Services.Interfaces;

public interface ILivenessService
{
    /// <summary>Opens a session and returns the page to show. Needs the member's reference photo first.</summary>
    Task<LivenessStartDto> StartAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the verdict from the provider — never from the client — and records it. Calling it again
    /// for a finished session returns the same verdict.
    /// </summary>
    Task<LivenessResultDto> CompleteAsync(Guid userId, string sessionId, CancellationToken cancellationToken);

    Task<LivenessResultDto?> GetLatestAsync(Guid userId, CancellationToken cancellationToken);
}
