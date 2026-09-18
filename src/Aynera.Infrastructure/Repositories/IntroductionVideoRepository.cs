using AutoMapper;
using Aynera.Application.Features.Videos.Repositories;
using Aynera.Domain.Photos.Enums;
using Aynera.Domain.Videos.Records;
using Aynera.Domain.Videos.Exceptions;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class IntroductionVideoRepository : IIntroductionVideoRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<IntroductionVideoRepository> _logger;

    public IntroductionVideoRepository(AyneraDbContext db, IMapper mapper, ILogger<IntroductionVideoRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IntroductionVideoRecord?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await _db.MemberIntroductionVideos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        return entity is null ? null : _mapper.Map<IntroductionVideoRecord>(entity);
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

        // Ignore query filter so we can revive a soft-deleted row for the same user.
        var entity = await _db.MemberIntroductionVideos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.UserId == video.UserId, cancellationToken);

        if (entity is null)
        {
            entity = new MemberIntroductionVideo { UserId = video.UserId };
            _db.MemberIntroductionVideos.Add(entity);
            entity.CreatedAtUtc = video.CreatedAtUtc == default ? DateTimeOffset.UtcNow : video.CreatedAtUtc;
        }
        else
        {
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (entity.CreatedAtUtc == default)
            {
                entity.CreatedAtUtc = video.CreatedAtUtc == default ? DateTimeOffset.UtcNow : video.CreatedAtUtc;
            }
        }

        entity.ContentType = video.ContentType;
        entity.ByteSize = video.ByteSize;
        entity.Data = video.Data;
        entity.FaceMatchStatus = status;
        entity.FaceMatchScore = video.FaceMatchScore;
        entity.GuidelinePassed = video.GuidelinePassed;
        entity.GuidelineDetail = video.GuidelineDetail;
        entity.Transcript = video.Transcript;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<IntroductionVideoRecord>(entity);
    }

    public async Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("IntroductionVideo SoftDeleteByUserIdAsync {UserId}", userId);
        var now = DateTimeOffset.UtcNow;
        await _db.MemberIntroductionVideos
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
    }
}
