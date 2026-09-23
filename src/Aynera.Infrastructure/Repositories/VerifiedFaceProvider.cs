using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Application.Features.Media.Storage;
using Aynera.Domain.Liveness.Enums;
using Aynera.Domain.Media.Enums;
using Aynera.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aynera.Infrastructure.Repositories;

public sealed class VerifiedFaceProvider : IVerifiedFaceProvider
{
    private readonly AyneraDbContext _db;
    private readonly IMediaStorage _storage;

    public VerifiedFaceProvider(AyneraDbContext db, IMediaStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<VerifiedFace?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The latest finished check decides: a later failed re-check withdraws the verified face.
        var latest = await _db.LivenessSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.CompletedAtUtc != null)
            .OrderByDescending(x => x.CompletedAtUtc)
            .Select(x => (LivenessOutcome?)x.Outcome)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest != LivenessOutcome.Passed)
        {
            return null;
        }

        var key = await _db.MemberMedia.AsNoTracking()
            .Where(x => x.UserId == userId && x.Kind == MediaKind.Liveness)
            .Select(x => x.StorageKey)
            .FirstOrDefaultAsync(cancellationToken);
        if (key is null)
        {
            return null;
        }

        var data = await _storage.GetAsync(key, cancellationToken);
        return data is { Length: > 0 } ? new VerifiedFace(data, "image/jpeg") : null;
    }
}
