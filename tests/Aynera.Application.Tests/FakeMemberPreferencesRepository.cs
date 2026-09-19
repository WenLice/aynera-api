using Aynera.Application.Features.Preferences.Repositories;
using Aynera.Domain.Preferences.Records;

namespace Aynera.Application.Tests;

/// <summary>In-memory preferences, so admission and lifecycle tests can decide who has them.</summary>
internal sealed class FakeMemberPreferencesRepository : IMemberPreferencesRepository
{
    private readonly Dictionary<Guid, MemberPreferencesRecord> _byUserId = new();

    /// <summary>Gives a member the minimum preferences the submit gate requires.</summary>
    public void Set(Guid userId) =>
        _byUserId[userId] = new MemberPreferencesRecord(userId, "Everyone", 24, 32, false, "Intent", "Prospect");

    public void Remove(Guid userId) => _byUserId.Remove(userId);

    public Task<MemberPreferencesRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_byUserId.GetValueOrDefault(userId));

    public Task<MemberPreferencesRecord> UpsertAsync(
        MemberPreferencesRecord preferences,
        CancellationToken cancellationToken)
    {
        _byUserId[preferences.UserId] = preferences;
        return Task.FromResult(preferences);
    }

    public Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _byUserId.Remove(userId);
        return Task.CompletedTask;
    }
}
