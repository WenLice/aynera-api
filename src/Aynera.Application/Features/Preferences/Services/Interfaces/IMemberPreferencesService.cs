using Aynera.Domain.Preferences.Requests;
using Aynera.Domain.Preferences.Responses;

namespace Aynera.Application.Features.Preferences.Services.Interfaces;

public interface IMemberPreferencesService
{
    /// <summary>Null until the member has saved them once.</summary>
    Task<MemberPreferencesDto?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Writes the member's matching preferences. A full replace.</summary>
    Task<MemberPreferencesDto> SaveAsync(
        Guid userId,
        UpdateMemberPreferencesRequest request,
        CancellationToken cancellationToken);
}
