using Elaris.Domain.Auth.Records;

namespace Elaris.Application.Features.Auth.Repositories;

public interface IRefreshSessionRepository
{
    Task AddAsync(RefreshSessionRecord session, CancellationToken cancellationToken);
    Task<RefreshSessionRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task MarkReplacedAsync(Guid sessionId, Guid replacedBySessionId, CancellationToken cancellationToken);
    Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken);
    Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task SoftDeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken);
}
