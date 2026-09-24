using Aynera.Domain.Settings.Records;

namespace Aynera.Application.Features.Settings.Repositories;

public interface IMemberSettingsRepository
{
    /// <summary>The member's settings, or null when nothing has been set yet.</summary>
    Task<MemberSettingsRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Writes the whole record, creating the row on the first save.</summary>
    Task<MemberSettingsRecord> UpsertAsync(MemberSettingsRecord settings, CancellationToken cancellationToken);

    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
