using AutoMapper;
using Aynera.Application.Features.Venues.Repositories;
using Aynera.Domain.Venues.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class VenueRepository : IVenueRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<VenueRepository> _logger;

    public VenueRepository(AyneraDbContext db, IMapper mapper, ILogger<VenueRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VenueRecord>> ListAllAsync(CancellationToken cancellationToken)
    {
        var entities = await _db.Venues
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<VenueRecord>>(entities);
    }

    public async Task<VenueRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Venues
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity is null ? null : _mapper.Map<VenueRecord>(entity);
    }

    public async Task<VenueRecord> AddAsync(VenueRecord venue, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Venue AddAsync {Name}", venue.Name);
        var entity = _mapper.Map<Venue>(venue);
        _db.Venues.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<VenueRecord>(entity);
    }

    public async Task<VenueRecord> UpdateAsync(VenueRecord venue, CancellationToken cancellationToken)
    {
        var entity = await _db.Venues
            .FirstOrDefaultAsync(x => x.Id == venue.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Venue {venue.Id} was not found.");

        if (entity.IsActive && !venue.IsActive)
        {
            entity.DeactivatedAtUtc = DateTimeOffset.UtcNow;
        }
        else if (!entity.IsActive && venue.IsActive)
        {
            entity.DeactivatedAtUtc = null;
        }

        _mapper.Map(venue, entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<VenueRecord>(entity);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Venue SoftDeleteAsync {VenueId}", id);
        var now = DateTimeOffset.UtcNow;
        await _db.Venues
            .Where(x => x.Id == id && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsDeleted, true)
                    .SetProperty(x => x.DeletedAtUtc, now)
                    .SetProperty(x => x.IsActive, false)
                    .SetProperty(x => x.DeactivatedAtUtc, now)
                    .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);
    }
}
