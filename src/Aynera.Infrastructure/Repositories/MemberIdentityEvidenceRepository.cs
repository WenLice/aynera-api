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
        // One query across photos and both videos. Only metadata is read — the bytes are in
        // object storage and are never needed to answer this.
        return await _db.MemberMedia
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId && x.FaceMatchStatus == FaceMatchStatus.Rejected,
                cancellationToken);
    }
}
