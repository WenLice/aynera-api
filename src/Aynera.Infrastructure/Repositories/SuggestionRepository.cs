using AutoMapper;
using Aynera.Application.Features.Suggestions.Repositories;
using Aynera.Domain.Suggestions.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Repositories;

public sealed class SuggestionRepository : ISuggestionRepository
{
    private readonly AyneraDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<SuggestionRepository> _logger;

    public SuggestionRepository(AyneraDbContext db, IMapper mapper, ILogger<SuggestionRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<SuggestionRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.Suggestions.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (_mapper.Map<List<SuggestionRecord>>(entities), totalCount);
    }

    public async Task<SuggestionRecord> AddAsync(
        SuggestionRecord suggestion,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Suggestion AddAsync");
        var entity = _mapper.Map<Suggestion>(suggestion);
        _db.Suggestions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<SuggestionRecord>(entity);
    }
}
