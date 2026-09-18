using Aynera.Application.Features.Profiles.Repositories;
using AutoMapper;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class MemberProfileRepository : IMemberProfileRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<MemberProfileRepository> _logger;

    public MemberProfileRepository(AyneraDbContext db, IMapper mapper, ILogger<MemberProfileRepository> logger)
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
            throw new AuthException("invalid_gender", "Gender must be Male, Female, Other, or PreferNotToSay.");
        }

        var entity = _mapper.Map<MemberProfile>(profile);
        entity.Gender = gender;

        _db.MemberProfiles.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<MemberProfileRecord>(entity);
    }

    public async Task<MemberProfileRecord> UpsertAsync(
        MemberProfileRecord profile,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberProfile UpsertAsync user {UserId}", profile.UserId);
        if (!Enum.TryParse<Gender>(profile.Gender, ignoreCase: true, out var gender))
        {
            throw new AuthException("invalid_gender", "Gender must be Male, Female, Other, or PreferNotToSay.");
        }

        var existing = await _db.MemberProfiles
            .FirstOrDefaultAsync(x => x.UserId == profile.UserId, cancellationToken);

        if (existing is null)
        {
            return await CreateAsync(profile, cancellationToken);
        }

        existing.Name = profile.Name.Trim();
        existing.Nickname = Normalize(profile.Nickname);
        existing.Gender = gender;
        existing.GenderIsPublic = profile.GenderIsPublic;
        existing.DateOfBirth = profile.DateOfBirth;
        existing.City = profile.City.Trim();
        existing.CityId = profile.CityId;
        existing.HeightCm = profile.HeightCm;
        existing.Hometown = Normalize(profile.Hometown);
        existing.Work = Normalize(profile.Work);
        existing.Religion = Normalize(profile.Religion);
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<MemberProfileRecord>(existing);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
