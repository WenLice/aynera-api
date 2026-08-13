using AutoMapper;
using Elaris.Application.Features.Suggestions.Repositories;
using Elaris.Domain.Suggestions.Records;
using Elaris.Persistence;
using Elaris.Persistence.Entities;
using Microsoft.Extensions.Logging;

namespace Elaris.Infrastructure.Repositories;

public sealed class SuggestionRepository : ISuggestionRepository
{
    private readonly ElarisDbContext _db;
    private readonly IMapper _mapper;
    private readonly ILogger<SuggestionRepository> _logger;

    public SuggestionRepository(ElarisDbContext db, IMapper mapper, ILogger<SuggestionRepository> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
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
