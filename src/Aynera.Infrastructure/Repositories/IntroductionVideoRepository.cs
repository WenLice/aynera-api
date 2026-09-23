using Aynera.Application.Features.Media.Storage;
using Aynera.Application.Features.Videos.Repositories;
using Aynera.Domain.Media.Enums;
using Aynera.Domain.Media.Statics;
using Aynera.Domain.Photos.Enums;
using Aynera.Domain.Videos.Records;
using Aynera.Domain.Videos.Exceptions;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

/// <summary>
/// The introduction video as a <see cref="MemberMedia"/> row of kind <see cref="MediaKind.IntroVideo"/>,
/// one per member, with the bytes at <c>{userId}/intro_video.{ext}</c>.
/// <para>
/// A re-upload revives and rewrites the same row. Soft deletes never touch the bucket, for the
/// same reason as photos: account deletion runs them inside a database transaction.
/// </para>
/// </summary>
public sealed class IntroductionVideoRepository : IIntroductionVideoRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMediaStorage _storage;
    private readonly ILogger<IntroductionVideoRepository> _logger;

    public IntroductionVideoRepository(
        AyneraDbContext db,
        IMediaStorage storage,
        ILogger<IntroductionVideoRepository> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    private IQueryable<MemberMedia> Videos(Guid userId) =>
        _db.MemberMedia.Where(x => x.UserId == userId && x.Kind == MediaKind.IntroVideo);

    public async Task<IntroductionVideoRecord?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await Videos(userId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToRecord(entity, []);
    }

    public async Task<byte[]?> ReadContentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = await Videos(userId)
            .AsNoTracking()
            .Select(x => x.StorageKey)
            .FirstOrDefaultAsync(cancellationToken);

        return key is null ? null : await _storage.GetAsync(key, cancellationToken);
    }

    public async Task<IntroductionVideoRecord> UpsertAsync(
        IntroductionVideoRecord video,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("IntroductionVideo UpsertAsync user {UserId}", video.UserId);
        if (!Enum.TryParse<FaceMatchStatus>(video.FaceMatchStatus, ignoreCase: true, out var status))
        {
            throw new VideoException("invalid_face_status", "Invalid face match status.");
        }

        var key = MediaKeys.IntroVideo(video.UserId, video.ContentType);

        // The file first: until the row points at it, nobody reads it, so a failed database write
        // leaves at worst an unreferenced file under the member's own folder.
        await _storage.PutAsync(key, video.Data, video.ContentType, cancellationToken);

        // Ignore the query filter so a soft-deleted row for this member is revived, not duplicated.
        var entity = await _db.MemberMedia
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.UserId == video.UserId && x.Kind == MediaKind.IntroVideo,
                cancellationToken);

        string? replacedKey = null;
        if (entity is null)
        {
            entity = new MemberMedia
            {
                Id = Guid.NewGuid(),
                UserId = video.UserId,
                Kind = MediaKind.IntroVideo,
                CreatedAtUtc = video.CreatedAtUtc == default ? DateTimeOffset.UtcNow : video.CreatedAtUtc,
            };
            _db.MemberMedia.Add(entity);
        }
        else
        {
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (!string.Equals(entity.StorageKey, key, StringComparison.Ordinal))
            {
                // A .mov replaced by a .mp4 lives under a different name; the old file is now orphaned.
                replacedKey = entity.StorageKey;
            }
        }

        entity.StorageKey = key;
        entity.ContentType = video.ContentType;
        entity.Caption = video.Caption;
        entity.ByteSize = video.ByteSize;
        entity.FaceMatchStatus = status;
        entity.FaceMatchScore = video.FaceMatchScore;
        entity.GuidelinePassed = video.GuidelinePassed;
        entity.GuidelineDetail = video.GuidelineDetail;
        entity.Transcript = video.Transcript;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;

        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(replacedKey))
        {
            await _storage.DeleteAsync(replacedKey, cancellationToken);
        }

        return ToRecord(entity, video.Data);
    }

    public async Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("IntroductionVideo SoftDeleteByUserIdAsync {UserId}", userId);
        var now = DateTimeOffset.UtcNow;
        await Videos(userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
    }

    public async Task UpdateCaptionAsync(Guid userId, string? caption, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await Videos(userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Caption, caption)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        if (updated == 0)
        {
            throw new VideoException("video_not_found", "Introduction video not found.", statusCode: 404);
        }
    }

    private static IntroductionVideoRecord ToRecord(MemberMedia entity, byte[] data) =>
        new(
            entity.UserId,
            entity.ContentType,
            entity.ByteSize,
            data,
            entity.FaceMatchStatus.ToString(),
            entity.FaceMatchScore,
            entity.GuidelinePassed ?? false,
            entity.GuidelineDetail,
            entity.Transcript,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.Caption,
            entity.StorageKey);
}
