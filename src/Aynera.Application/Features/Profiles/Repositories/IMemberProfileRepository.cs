using Aynera.Domain.Auth.Records;

namespace Aynera.Application.Features.Profiles.Repositories;

public interface IMemberProfileRepository
{
    Task<MemberProfileRecord> CreateAsync(MemberProfileRecord profile, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the member's basic details, creating the row when there is none yet (the app's registration
    /// path reaches this with a phone-verified account and no profile). A full replace of every field.
    /// </summary>
    Task<MemberProfileRecord> UpsertAsync(MemberProfileRecord profile, CancellationToken cancellationToken);
    Task<MemberProfileRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
