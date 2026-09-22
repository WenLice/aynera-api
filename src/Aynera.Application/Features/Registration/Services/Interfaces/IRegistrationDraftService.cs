using Aynera.Domain.Registration.Requests;
using Aynera.Domain.Registration.Responses;

namespace Aynera.Application.Features.Registration.Services.Interfaces;

public interface IRegistrationDraftService
{
    /// <summary>Where the member stands: answers so far, steps satisfied, and the first one outstanding.</summary>
    Task<RegistrationProgressDto> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Merges one page's answers and returns the updated progress. Writes to the draft while the
    /// profile does not exist and to the profile once it does; promotes automatically the moment
    /// the required set is complete. The caller never decides which of those happens.
    /// </summary>
    Task<RegistrationProgressDto> PatchAsync(
        Guid userId,
        UpdateRegistrationRequest request,
        CancellationToken cancellationToken);
}
