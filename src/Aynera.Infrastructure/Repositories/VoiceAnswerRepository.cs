using Aynera.Application.Features.Media.Storage;
using Aynera.Application.Features.Voice.Repositories;
using Aynera.Domain.Media.Enums;
using Aynera.Domain.Media.Statics;
using Aynera.Domain.Voice.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

/// <summary>
/// Spoken prompt answers as <see cref="MemberMedia"/> rows of kind <see cref="MediaKind.VoiceAnswer"/>,
/// one live row per prompt, with the bytes at <c>{userId}/voice_{promptId}.{ext}</c>.
/// <para>
/// A re-recording revives and rewrites the same row. Soft deletes never touch the bucket, for the
/// same reason as photos and the video: account deletion runs them inside a database transaction.
/// </para>
/// </summary>
public sealed class VoiceAnswerRepository : IVoiceAnswerRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMediaStorage _storage;
    private readonly ILogger<VoiceAnswerRepository> _logger;

    public VoiceAnswerRepository(
        AyneraDbContext db,
        IMediaStorage storage,
        ILogger<VoiceAnswerRepository> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    private IQueryable<MemberMedia> Answers(Guid userId) =>
        _db.MemberMedia.Where(x => x.UserId == userId && x.Kind == MediaKind.VoiceAnswer);

    public async Task<IReadOnlyList<VoiceAnswerRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = await Answers(userId)
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(x => ToRecord(x, [])).ToList();
    }

    public async Task<IReadOnlyList<string>> ListPromptIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await Answers(userId)
            .AsNoTracking()
            .Where(x => x.PromptId != null)
            .Select(x => x.PromptId!)
            .ToListAsync(cancellationToken);

    public async Task<VoiceAnswerRecord?> FindWithContentAsync(
        Guid userId,
        string promptId,
        CancellationToken cancellationToken)
    {
        var entity = await Answers(userId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PromptId == promptId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var data = await _storage.GetAsync(entity.StorageKey, cancellationToken);
        return ToRecord(entity, data ?? []);
    }

    public async Task<VoiceAnswerRecord> UpsertAsync(
        VoiceAnswerRecord answer,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("VoiceAnswer UpsertAsync user {UserId} prompt {PromptId}", answer.UserId, answer.PromptId);

        var key = MediaKeys.VoiceAnswer(answer.UserId, answer.PromptId, answer.ContentType);

        // The file first: until the row points at it, nobody reads it, so a failed database write
        // leaves at worst an unreferenced file under the member's own folder.
        await _storage.PutAsync(key, answer.Data, answer.ContentType, cancellationToken);

        // Ignore the query filter so a soft-deleted row for this prompt is revived, not duplicated.
        var entity = await _db.MemberMedia
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.UserId == answer.UserId
                     && x.Kind == MediaKind.VoiceAnswer
                     && x.PromptId == answer.PromptId,
                cancellationToken);

        string? replacedKey = null;
        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            entity = new MemberMedia
            {
                Id = Guid.NewGuid(),
                UserId = answer.UserId,
                Kind = MediaKind.VoiceAnswer,
                PromptId = answer.PromptId,
                CreatedAtUtc = now,
            };
            _db.MemberMedia.Add(entity);
        }
        else
        {
            entity.UpdatedAtUtc = now;
            if (!string.Equals(entity.StorageKey, key, StringComparison.Ordinal))
            {
                // A .webm replaced by a .m4a lives under a different name; the old file is now orphaned.
                replacedKey = entity.StorageKey;
            }
        }

        entity.StorageKey = key;
        entity.ContentType = answer.ContentType;
        entity.ByteSize = answer.ByteSize;
        entity.GuidelinePassed = answer.GuidelinePassed;
        entity.GuidelineDetail = answer.GuidelineDetail;
        entity.Transcript = answer.Transcript;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;

        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(replacedKey))
        {
            await _storage.DeleteAsync(replacedKey, cancellationToken);
        }

        return ToRecord(entity, answer.Data);
    }

    public async Task<bool> SoftDeleteAsync(Guid userId, string promptId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await Answers(userId)
            .Where(x => x.PromptId == promptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
        return updated > 0;
    }

    public async Task SoftDeleteExceptAsync(
        Guid userId,
        IReadOnlyCollection<string> keepPromptIds,
        CancellationToken cancellationToken)
    {
        var keep = keepPromptIds.ToList();
        var now = DateTimeOffset.UtcNow;
        await Answers(userId)
            .Where(x => x.PromptId == null || !keep.Contains(x.PromptId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
    }

    public async Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("VoiceAnswer SoftDeleteByUserIdAsync {UserId}", userId);
        await SoftDeleteExceptAsync(userId, [], cancellationToken);
    }

    private static VoiceAnswerRecord ToRecord(MemberMedia entity, byte[] data) =>
        new(
            entity.UserId,
            entity.PromptId ?? string.Empty,
            entity.ContentType,
            entity.ByteSize,
            data,
            entity.GuidelinePassed ?? false,
            entity.GuidelineDetail,
            entity.Transcript,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.StorageKey);
}
