using Elaris.Domain.Suggestions.Records;

namespace Elaris.Application.Features.Suggestions.Repositories;

public interface ISuggestionRepository
{
    Task<(IReadOnlyList<SuggestionRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<SuggestionRecord> AddAsync(SuggestionRecord suggestion, CancellationToken cancellationToken);
}
