using Aynera.Application.Features.Liveness.Repositories;
using Aynera.Application.Features.Media.Storage;
using Aynera.Domain.Liveness.Enums;
using Aynera.Domain.Liveness.Records;
using Aynera.Domain.Media.Enums;
using Aynera.Domain.Media.Statics;
using Aynera.Domain.Photos.Enums;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aynera.Infrastructure.Repositories;

public sealed class LivenessRepository : ILivenessRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMediaStorage _storage;

    public LivenessRepository(AyneraDbContext db, IMediaStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task AddSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken)
    {
        _db.LivenessSessions.Add(new LivenessSession
        {
            SessionId = session.SessionId,
            UserId = session.UserId,
            Outcome = Enum.Parse<LivenessOutcome>(session.Outcome),
            CreatedAtUtc = session.CreatedAtUtc,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LivenessSessionRecord?> FindSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var entity = await _db.LivenessSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task UpdateSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken)
    {
        var entity = await _db.LivenessSessions.FirstAsync(x => x.SessionId == session.SessionId, cancellationToken);
        entity.Outcome = Enum.Parse<LivenessOutcome>(session.Outcome);
        entity.Confidence = session.Confidence;
        entity.Similarity = session.Similarity;
        entity.CompletedAtUtc = session.CompletedAtUtc;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LivenessSessionRecord?> FindLatestCompletedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await _db.LivenessSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.CompletedAtUtc != null)
            .OrderByDescending(x => x.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task SaveSelfieAsync(
        Guid userId,
        byte[] jpeg,
        string faceMatchStatus,
        decimal? similarity,
        CancellationToken cancellationToken)
    {
        var key = MediaKeys.Liveness(userId);
        await _storage.PutAsync(key, jpeg, "image/jpeg", cancellationToken);

        // One live selfie per member: a re-check revives and rewrites the same row, so an earlier
        // mismatch stops counting against them once they pass.
        var entity = await _db.MemberMedia.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Kind == MediaKind.Liveness, cancellationToken);
        if (entity is null)
        {
            entity = new MemberMedia { Id = Guid.NewGuid(), UserId = userId, Kind = MediaKind.Liveness };
            _db.MemberMedia.Add(entity);
        }
        else
        {
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        entity.StorageKey = key;
        entity.ContentType = "image/jpeg";
        entity.ByteSize = jpeg.Length;
        entity.FaceMatchStatus = Enum.Parse<FaceMatchStatus>(faceMatchStatus, ignoreCase: true);
        entity.FaceMatchScore = similarity;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static LivenessSessionRecord ToRecord(LivenessSession entity) =>
        new(
            entity.SessionId,
            entity.UserId,
            entity.Outcome.ToString(),
            entity.Confidence,
            entity.Similarity,
            entity.CreatedAtUtc,
            entity.CompletedAtUtc);
}
