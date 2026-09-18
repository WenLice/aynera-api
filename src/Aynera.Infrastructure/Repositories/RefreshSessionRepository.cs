using AutoMapper;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class RefreshSessionRepository : IRefreshSessionRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<RefreshSessionRepository> _logger;

    public RefreshSessionRepository(AyneraDbContext db, IMapper mapper, ILogger<RefreshSessionRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task AddAsync(RefreshSessionRecord session, CancellationToken cancellationToken)
    {
        _logger.LogDebug("RefreshSession AddAsync user {UserId}", session.UserId);
        _db.RefreshSessions.Add(_mapper.Map<RefreshSession>(session));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshSessionRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var entity = await _db.RefreshSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        return entity is null ? null : _mapper.Map<RefreshSessionRecord>(entity);
    }

    public async Task MarkReplacedAsync(Guid sessionId, Guid replacedBySessionId, CancellationToken cancellationToken)
    {
        var entity = await _db.RefreshSessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.ReplacedAtUtc = DateTimeOffset.UtcNow;
        entity.ReplacedBySessionId = replacedBySessionId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var entity = await _db.RefreshSessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.RevokedAtUtc ??= DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.RefreshSessions
            .Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.RevokedAtUtc, now),
                cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.RefreshSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.RevokedAtUtc, now),
                cancellationToken);
    }

    public async Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.RefreshSessions
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.RevokedAtUtc, now),
                cancellationToken);
    }
}
