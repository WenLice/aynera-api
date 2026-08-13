using Elaris.Domain.Auth.Records;

namespace Elaris.Application.Features.Auth.Repositories;

public interface IMemberProfileRepository
{
    Task<MemberProfileRecord> CreateAsync(MemberProfileRecord profile, CancellationToken cancellationToken);
    Task<MemberProfileRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
