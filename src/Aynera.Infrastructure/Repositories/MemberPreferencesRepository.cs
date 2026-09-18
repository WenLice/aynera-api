using Aynera.Application.Features.Preferences.Repositories;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Preferences.Enums;
using Aynera.Domain.Preferences.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class MemberPreferencesRepository : IMemberPreferencesRepository
{
    private readonly AyneraDbContext _db;
    private readonly ILogger<MemberPreferencesRepository> _logger;

    public MemberPreferencesRepository(
        AyneraDbContext db,
        ILogger<MemberPreferencesRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<MemberPreferencesRecord?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await _db.MemberPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        return entity is null ? null : ToRecord(entity);
    }

    public async Task<MemberPreferencesRecord> UpsertAsync(
        MemberPreferencesRecord preferences,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberPreferences UpsertAsync user {UserId}", preferences.UserId);

        if (!Enum.TryParse<InterestedIn>(preferences.InterestedIn, ignoreCase: true, out var interestedIn))
        {
            throw new AuthException(
                "invalid_interested_in",
                "Interested in must be Male, Female, Other, or Everyone.");
        }

        if (!Enum.TryParse<IntentOutcome>(preferences.IntentOutcome, ignoreCase: true, out var outcome))
        {
            throw new AuthException(
                "invalid_intent_outcome",
                "Intent outcome must be Platonic, Spontaneous, Prospect, or Legacy.");
        }

        var existing = await _db.MemberPreferences
            .FirstOrDefaultAsync(x => x.UserId == preferences.UserId, cancellationToken);

        if (existing is null)
        {
            existing = new MemberPreferences
            {
                UserId = preferences.UserId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            _db.MemberPreferences.Add(existing);
        }
        else
        {
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        existing.InterestedIn = interestedIn;
        existing.MinAge = preferences.MinAge;
        existing.MaxAge = preferences.MaxAge;
        existing.AgeIsFlexible = preferences.AgeIsFlexible;
        existing.IntentOutcome = outcome;

        await _db.SaveChangesAsync(cancellationToken);
        return ToRecord(existing);
    }

    public async Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("MemberPreferences SoftDeleteByUserIdAsync {UserId}", userId);
        var now = DateTimeOffset.UtcNow;
        await _db.MemberPreferences
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
    }

    private static MemberPreferencesRecord ToRecord(MemberPreferences entity) =>
        new(
            entity.UserId,
            entity.InterestedIn.ToString(),
            entity.MinAge,
            entity.MaxAge,
            entity.AgeIsFlexible,
            entity.IntentOutcome.ToString());
}
