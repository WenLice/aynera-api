using Aynera.Application.Features.Settings.Repositories;
using Aynera.Domain.Settings.Records;

namespace Aynera.Application.Tests;

/// <summary>In-memory member settings.</summary>
internal sealed class FakeMemberSettingsRepository : IMemberSettingsRepository
{
    private readonly Dictionary<Guid, MemberSettingsRecord> _rows = new();

    public Task<MemberSettingsRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.GetValueOrDefault(userId));

    public Task<MemberSettingsRecord> UpsertAsync(MemberSettingsRecord settings, CancellationToken cancellationToken)
    {
        _rows[settings.UserId] = settings;
        return Task.FromResult(settings);
    }

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _rows.Remove(userId);
        return Task.CompletedTask;
    }
}
