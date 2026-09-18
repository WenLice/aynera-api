using Aynera.Domain.Preferences.Records;

namespace Aynera.Application.Features.Preferences.Repositories;

public interface IMemberPreferencesRepository
{
    Task<MemberPreferencesRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Writes the member's preferences, creating the row on the first save. A full replace.</summary>
    Task<MemberPreferencesRecord> UpsertAsync(
        MemberPreferencesRecord preferences,
        CancellationToken cancellationToken);

    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
