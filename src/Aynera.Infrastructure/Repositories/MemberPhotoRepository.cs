using Aynera.Application.Features.Media.Storage;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Domain.Media.Enums;
using Aynera.Domain.Media.Statics;
using Aynera.Domain.Photos.Records;
using Aynera.Domain.Photos.Enums;
using Aynera.Domain.Photos.Exceptions;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

/// <summary>
/// Photos as <see cref="MemberMedia"/> rows of kind <see cref="MediaKind.Photo"/>, with the bytes in
/// object storage at <c>{userId}/photo_{index}.jpg</c>.
/// <para>
/// A record's <c>SortOrder</c> is the 1-based slot, the same number as the file name. Bytes are
/// loaded only where a caller needs them (the reference photo for face matching, a single photo for
/// display); listings carry an empty <c>Data</c>.
/// </para>
/// <para>
/// Soft deletes never touch the bucket. They can run inside the account-deletion transaction, which
/// object storage cannot join — and a reused slot overwrites its own file anyway.
/// </para>
/// </summary>
public sealed class MemberPhotoRepository : IMemberPhotoRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMediaStorage _storage;
    private readonly ILogger<MemberPhotoRepository> _logger;

    public MemberPhotoRepository(
        AyneraDbContext db,
        IMediaStorage storage,
        ILogger<MemberPhotoRepository> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    private IQueryable<MemberMedia> Photos(Guid userId) =>
        _db.MemberMedia.Where(x => x.UserId == userId && x.Kind == MediaKind.Photo);

    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Photos(userId).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<MemberPhotoRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entities = await Photos(userId)
            .AsNoTracking()
            .OrderBy(x => x.Index)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return entities.Select(x => ToRecord(x, [])).ToList();
    }

    public async Task<MemberPhotoRecord?> FindByIdAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var entity = await Photos(userId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == photoId, cancellationToken);

        return entity is null ? null : ToRecord(entity, await ReadAsync(entity, cancellationToken));
    }

    public async Task<MemberPhotoRecord?> FindReferenceAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await Photos(userId)
            .AsNoTracking()
            .Where(x => x.IsReference)
            .OrderBy(x => x.Index)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToRecord(entity, await ReadAsync(entity, cancellationToken));
    }

    /// <summary>
    /// The lowest free slot, starting at 1. Filling the gap a deleted photo left keeps the file
    /// names at <c>photo_1</c>…<c>photo_N</c> instead of climbing past the slots the app shows.
    /// </summary>
    public async Task<int> NextSortOrderAsync(Guid userId, CancellationToken cancellationToken)
    {
        var taken = await Photos(userId)
            .Where(x => x.Index != null)
            .Select(x => x.Index!.Value)
            .ToListAsync(cancellationToken);

        var index = 1;
        while (taken.Contains(index))
        {
            index++;
        }

        return index;
    }

    public async Task<MemberPhotoRecord> AddAsync(
        MemberPhotoRecord photo,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberPhoto AddAsync user {UserId} slot {Index}", photo.UserId, photo.SortOrder);
        if (!Enum.TryParse<FaceMatchStatus>(photo.FaceMatchStatus, ignoreCase: true, out var status))
        {
            throw new PhotoException("invalid_face_status", "Invalid face match status.");
        }

        if (photo.SortOrder < 1)
        {
            throw new PhotoException("invalid_photo_slot", "Photo slots start at 1.");
        }

        var entity = new MemberMedia
        {
            Id = photo.Id == Guid.Empty ? Guid.NewGuid() : photo.Id,
            UserId = photo.UserId,
            Kind = MediaKind.Photo,
            Index = photo.SortOrder,
            StorageKey = MediaKeys.Photo(photo.UserId, photo.SortOrder),
            ContentType = photo.ContentType,
            Caption = photo.Caption,
            ByteSize = photo.ByteSize,
            IsReference = photo.IsReference,
            FaceMatchStatus = status,
            FaceMatchScore = photo.FaceMatchScore,
            CreatedAtUtc = photo.CreatedAtUtc == default ? DateTimeOffset.UtcNow : photo.CreatedAtUtc,
        };

        // The row goes in first: the unique slot index is what stops two uploads claiming the same
        // slot. Writing the file first would let the loser overwrite the winner's photo before
        // finding out it lost.
        _db.MemberMedia.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _storage.PutAsync(entity.StorageKey, photo.Data, photo.ContentType, cancellationToken);
        }
        catch
        {
            // No file, no row: a photo the member cannot see must not hold their slot.
            _db.MemberMedia.Remove(entity);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return ToRecord(entity, photo.Data);
    }

    public async Task SoftDeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberPhoto SoftDeleteAsync {PhotoId} user {UserId}", photoId, userId);
        var now = DateTimeOffset.UtcNow;
        var updated = await Photos(userId)
            .Where(x => x.Id == photoId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now)
                    .SetProperty(x => x.IsReference, false),
                cancellationToken);

        if (updated == 0)
        {
            throw new PhotoException("photo_not_found", "Photo not found.", statusCode: 404);
        }
    }

    public async Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await Photos(userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now)
                    .SetProperty(x => x.IsReference, false),
                cancellationToken);
    }

    public async Task PromoteNextReferenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var next = await Photos(userId)
            .OrderBy(x => x.Index)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (next is null)
        {
            return;
        }

        next.IsReference = true;
        next.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCaptionAsync(
        Guid userId,
        Guid photoId,
        string? caption,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await Photos(userId)
            .Where(x => x.Id == photoId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Caption, caption)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        if (updated == 0)
        {
            throw new PhotoException("photo_not_found", "Photo not found.", statusCode: 404);
        }
    }

    private async Task<byte[]> ReadAsync(MemberMedia entity, CancellationToken cancellationToken) =>
        await _storage.GetAsync(entity.StorageKey, cancellationToken) ?? [];

    private static MemberPhotoRecord ToRecord(MemberMedia entity, byte[] data) =>
        new(
            entity.Id,
            entity.UserId,
            entity.Index ?? 0,
            entity.ContentType,
            entity.ByteSize,
            data,
            entity.IsReference,
            entity.FaceMatchStatus.ToString(),
            entity.FaceMatchScore,
            entity.CreatedAtUtc,
            entity.Caption,
            entity.StorageKey);
}
