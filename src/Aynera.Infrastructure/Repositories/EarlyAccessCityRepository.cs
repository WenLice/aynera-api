using AutoMapper;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class EarlyAccessCityRepository : IEarlyAccessCityRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<EarlyAccessCityRepository> _logger;

    public EarlyAccessCityRepository(AyneraDbContext db, IMapper mapper, ILogger<EarlyAccessCityRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EarlyAccessCityRecord>> ListOpenAsync(CancellationToken cancellationToken)
    {
        var entities = await _db.EarlyAccessCities
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<EarlyAccessCityRecord>>(entities);
    }

    public async Task<IReadOnlyList<EarlyAccessCityRecord>> ListAllAsync(CancellationToken cancellationToken)
    {
        var entities = await _db.EarlyAccessCities
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<EarlyAccessCityRecord>>(entities);
    }

    public async Task<EarlyAccessCityRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EarlyAccessCities
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity is null ? null : _mapper.Map<EarlyAccessCityRecord>(entity);
    }

    public async Task<EarlyAccessCityRecord?> FindOpenByNameAsync(string name, CancellationToken cancellationToken)
    {
        var entity = await _db.EarlyAccessCities
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IsActive && x.Name.ToLower() == name.ToLower(),
                cancellationToken);

        return entity is null ? null : _mapper.Map<EarlyAccessCityRecord>(entity);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken)
    {
        var query = _db.EarlyAccessCities.Where(x => x.Name.ToLower() == name.ToLower());
        if (excludingId is { } id)
        {
            query = query.Where(x => x.Id != id);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<EarlyAccessCityRecord> AddAsync(
        EarlyAccessCityRecord city,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("EarlyAccessCity AddAsync {Name}", city.Name);
        var entity = _mapper.Map<EarlyAccessCity>(city);
        _db.EarlyAccessCities.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<EarlyAccessCityRecord>(entity);
    }

    public async Task<EarlyAccessCityRecord> UpdateAsync(
        EarlyAccessCityRecord city,
        CancellationToken cancellationToken)
    {
        var entity = await _db.EarlyAccessCities
            .FirstOrDefaultAsync(x => x.Id == city.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Early access city {city.Id} was not found.");

        if (entity.IsActive && !city.IsActive)
        {
            entity.DeactivatedAtUtc = DateTimeOffset.UtcNow;
        }
        else if (!entity.IsActive && city.IsActive)
        {
            entity.DeactivatedAtUtc = null;
        }

        _mapper.Map(city, entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<EarlyAccessCityRecord>(entity);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("EarlyAccessCity SoftDeleteAsync {CityId}", id);
        var now = DateTimeOffset.UtcNow;
        await _db.EarlyAccessCities
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
