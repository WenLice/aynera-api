using Elaris.Domain.Suggestions.Records;

namespace Elaris.Application.Features.Suggestions.Repositories;

public interface ISuggestionRepository
{
    Task<SuggestionRecord> AddAsync(SuggestionRecord suggestion, CancellationToken cancellationToken);
}
