using AutoMapper;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Domain.EarlyAccess.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class EarlyAccessSignupRepository : IEarlyAccessSignupRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<EarlyAccessSignupRepository> _logger;

    public EarlyAccessSignupRepository(AyneraDbContext db, IMapper mapper, ILogger<EarlyAccessSignupRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<EarlyAccessSignupRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.EarlyAccessSignups.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (_mapper.Map<List<EarlyAccessSignupRecord>>(entities), totalCount);
    }

    public async Task<EarlyAccessSignupRecord?> FindByEmailAsync(
        string emailNormalized,
        CancellationToken cancellationToken)
    {
        var entity = await _db.EarlyAccessSignups
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Email == emailNormalized && !x.IsDeleted, cancellationToken);

        return entity is null ? null : _mapper.Map<EarlyAccessSignupRecord>(entity);
    }

    public async Task<EarlyAccessSignupRecord> AddAsync(
        EarlyAccessSignupRecord signup,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("EarlyAccessSignup AddAsync");
        var entity = _mapper.Map<EarlyAccessSignup>(signup);
        _db.EarlyAccessSignups.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<EarlyAccessSignupRecord>(entity);
    }

    public async Task<EarlyAccessSignupRecord> UpdateAsync(
        EarlyAccessSignupRecord signup,
        CancellationToken cancellationToken)
    {
        var entity = await _db.EarlyAccessSignups
            .FirstOrDefaultAsync(x => x.Id == signup.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Early access signup {signup.Id} was not found.");

        _mapper.Map(signup, entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<EarlyAccessSignupRecord>(entity);
    }
}
