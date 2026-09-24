using System.Text.Json;
using Aynera.Application.Features.Settings.Repositories;
using Aynera.Domain.Settings.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

/// <summary>The member's settings row: notification switches, pause, and field visibility.</summary>
public sealed class MemberSettingsRepository : IMemberSettingsRepository
{
    private readonly AyneraDbContext _db;
    private readonly ILogger<MemberSettingsRepository> _logger;

    public MemberSettingsRepository(AyneraDbContext db, ILogger<MemberSettingsRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<MemberSettingsRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await _db.MemberSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<MemberSettingsRecord> UpsertAsync(MemberSettingsRecord settings, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberSettings UpsertAsync user {UserId}", settings.UserId);
        var entity = await MemberSettingsRows.GetOrAddAsync(_db, settings.UserId, cancellationToken);

        entity.NotifyIntroductions = settings.NotifyIntroductions;
        entity.NotifyReplies = settings.NotifyReplies;
        entity.NotifyWeekendSurprise = settings.NotifyWeekendSurprise;
        entity.IntroductionsPaused = settings.IntroductionsPaused;
        entity.PausedAtUtc = settings.PausedAtUtc;
        entity.Visibility = MemberSettingsRows.Write(settings.Visibility);

        await _db.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberSettings SoftDeleteByUserIdAsync {UserId}", userId);
        var now = DateTimeOffset.UtcNow;
        await _db.MemberSettings
            .Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
    }

    private static MemberSettingsRecord ToRecord(MemberSettings entity) =>
        new(
            entity.UserId,
            entity.NotifyIntroductions,
            entity.NotifyReplies,
            entity.NotifyWeekendSurprise,
            entity.IntroductionsPaused,
            entity.PausedAtUtc,
            MemberSettingsRows.Read(entity.Visibility));
}

/// <summary>
/// Shared by every repository that stores a visibility choice — the profile's gender, each
/// everyday and belief answer — so they all write the one settings row, in the caller's own
/// <c>SaveChanges</c>.
/// </summary>
internal static class MemberSettingsRows
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The tracked settings row, created if missing. A soft-deleted row is revived rather than
    /// duplicated, since the member id is the key.
    /// </summary>
    public static async Task<MemberSettings> GetOrAddAsync(
        AyneraDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = db.MemberSettings.Local.FirstOrDefault(x => x.UserId == userId)
            ?? await db.MemberSettings.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            entity = new MemberSettings { UserId = userId, CreatedAtUtc = DateTimeOffset.UtcNow };
            db.MemberSettings.Add(entity);
            return entity;
        }

        if (entity.IsDeleted)
        {
            entity.IsDeleted = false;
            entity.DeletedAtUtc = null;
        }

        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return entity;
    }

    /// <summary>The member's visibility map, or an empty one (everything shown) when there is no row.</summary>
    public static async Task<Dictionary<string, bool>> ReadVisibilityAsync(
        AyneraDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var json = await db.MemberSettings.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.Visibility)
            .FirstOrDefaultAsync(cancellationToken);
        return json is null ? new Dictionary<string, bool>() : Read(json);
    }

    /// <summary>Sets some fields' visibility on the member's row, leaving every other field as it was.</summary>
    public static async Task SetVisibilityAsync(
        AyneraDbContext db,
        Guid userId,
        IEnumerable<KeyValuePair<string, bool>> changes,
        CancellationToken cancellationToken)
    {
        var entity = await GetOrAddAsync(db, userId, cancellationToken);
        var visibility = Read(entity.Visibility);
        foreach (var (field, shown) in changes)
        {
            visibility[field] = shown;
        }

        entity.Visibility = Write(visibility);
    }

    public static Dictionary<string, bool> Read(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, bool>>(json, Json) ?? new Dictionary<string, bool>();

    public static string Write(IReadOnlyDictionary<string, bool> visibility) =>
        JsonSerializer.Serialize(visibility, Json);
}
