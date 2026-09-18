using Aynera.Domain.Auth.Records;

namespace Aynera.Application.Features.Profiles.Repositories;

public interface IMemberProfileRepository
{
    Task<MemberProfileRecord> CreateAsync(MemberProfileRecord profile, CancellationToken cancellationToken);
    Task<MemberProfileRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
