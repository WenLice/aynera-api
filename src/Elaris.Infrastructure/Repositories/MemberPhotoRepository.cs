using AutoMapper;
using Elaris.Application.Features.Photos.Repositories;
using Elaris.Domain.Photos.Records;
using Elaris.Domain.Photos.Enums;
using Elaris.Domain.Photos.Exceptions;
using Elaris.Persistence;
using Elaris.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elaris.Infrastructure.Repositories;

public sealed class MemberPhotoRepository : IMemberPhotoRepository
{
    private readonly ElarisDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<MemberPhotoRepository> _logger;

    public MemberPhotoRepository(ElarisDbContext db, IMapper mapper, ILogger<MemberPhotoRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _db.MemberPhotos.CountAsync(x => x.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<MemberPhotoRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entities = await _db.MemberPhotos
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<MemberPhotoRecord>>(
            entities.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc).ToList());
    }

    public async Task<MemberPhotoRecord?> FindByIdAsync(
        Guid userId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var entity = await _db.MemberPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Id == photoId, cancellationToken);

        return entity is null ? null : _mapper.Map<MemberPhotoRecord>(entity);
    }

    public async Task<MemberPhotoRecord?> FindReferenceAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entities = await _db.MemberPhotos
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsReference)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var entity = entities.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc).FirstOrDefault();
        return entity is null ? null : _mapper.Map<MemberPhotoRecord>(entity);
    }

    public async Task<int> NextSortOrderAsync(Guid userId, CancellationToken cancellationToken)
    {
        var max = await _db.MemberPhotos
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(cancellationToken);

        return (max ?? -1) + 1;
    }

    public async Task<MemberPhotoRecord> AddAsync(
        MemberPhotoRecord photo,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberPhoto AddAsync user {UserId}", photo.UserId);
        if (!Enum.TryParse<FaceMatchStatus>(photo.FaceMatchStatus, ignoreCase: true, out var status))
        {
            throw new PhotoException("invalid_face_status", "Invalid face match status.");
        }

        var entity = _mapper.Map<MemberPhoto>(photo);
        entity.FaceMatchStatus = status;

        _db.MemberPhotos.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<MemberPhotoRecord>(entity);
    }

    public async Task SoftDeleteAsync(Guid userId, Guid photoId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberPhoto SoftDeleteAsync {PhotoId} user {UserId}", photoId, userId);
        var now = DateTimeOffset.UtcNow;
        var updated = await _db.MemberPhotos
            .Where(x => x.UserId == userId && x.Id == photoId && !x.IsDeleted)
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
        await _db.MemberPhotos
            .Where(x => x.UserId == userId && !x.IsDeleted)
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
        var candidates = await _db.MemberPhotos
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var next = candidates.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc).FirstOrDefault();
        if (next is null)
        {
            return;
        }

        next.IsReference = true;
        next.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
