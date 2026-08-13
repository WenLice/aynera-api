using AutoMapper;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Persistence;
using Elaris.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elaris.Infrastructure.Repositories;

public sealed class MemberProfileRepository : IMemberProfileRepository
{
    private readonly ElarisDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<MemberProfileRepository> _logger;

    public MemberProfileRepository(ElarisDbContext db, IMapper mapper, ILogger<MemberProfileRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<MemberProfileRecord> CreateAsync(
        MemberProfileRecord profile,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberProfile CreateAsync user {UserId}", profile.UserId);
        if (!Enum.TryParse<Gender>(profile.Gender, ignoreCase: true, out var gender))
        {
            throw new AuthException("invalid_gender", "Gender must be Male, Female, or Other.");
        }

        var entity = _mapper.Map<MemberProfile>(profile);
        entity.Gender = gender;

        _db.MemberProfiles.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<MemberProfileRecord>(entity);
    }

    public async Task<MemberProfileRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await _db.MemberProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        return entity is null ? null : _mapper.Map<MemberProfileRecord>(entity);
    }

    public async Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberProfile SoftDeleteByUserIdAsync {UserId}", userId);
        var now = DateTimeOffset.UtcNow;
        await _db.MemberProfiles
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
    }
}
