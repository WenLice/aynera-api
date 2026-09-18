using Aynera.Domain.Admissions.Records;

namespace Aynera.Application.Features.Admissions.Repositories;

public interface IMemberConsentRepository
{
    Task<IReadOnlyList<MemberConsentRecord>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Idempotent: re-accepting the same document version returns the existing row.</summary>
    Task<MemberConsentRecord> AcceptAsync(MemberConsentRecord consent, CancellationToken cancellationToken);
}
