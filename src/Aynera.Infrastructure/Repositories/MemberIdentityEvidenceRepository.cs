using Aynera.Application.Features.Admissions.Repositories;
using Aynera.Domain.Photos.Enums;
using Aynera.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aynera.Infrastructure.Repositories;

public sealed class MemberIdentityEvidenceRepository : IMemberIdentityEvidenceRepository
{
    private readonly AyneraDbContext _db;

    public MemberIdentityEvidenceRepository(AyneraDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasRejectedFaceMatchAsync(Guid userId, CancellationToken cancellationToken)
    {
        // AnyAsync so the stored photo/video bytes are never materialized for this check.
        var photoRejected = await _db.MemberPhotos
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId && x.FaceMatchStatus == FaceMatchStatus.Rejected,
                cancellationToken);

        if (photoRejected)
        {
            return true;
        }

        return await _db.MemberIntroductionVideos
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId && x.FaceMatchStatus == FaceMatchStatus.Rejected,
                cancellationToken);
    }
}
