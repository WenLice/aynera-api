using Elaris.Domain.EarlyAccess.Records;

namespace Elaris.Application.Features.EarlyAccess.Repositories;

public interface IEarlyAccessSignupRepository
{
    Task<(IReadOnlyList<EarlyAccessSignupRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<EarlyAccessSignupRecord?> FindByEmailAsync(string emailNormalized, CancellationToken cancellationToken);

    Task<EarlyAccessSignupRecord> AddAsync(EarlyAccessSignupRecord signup, CancellationToken cancellationToken);

    Task<EarlyAccessSignupRecord> UpdateAsync(EarlyAccessSignupRecord signup, CancellationToken cancellationToken);
}
