using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Records;

namespace Aynera.Application.Features.Admissions.Repositories;

public interface IMemberAdmissionRepository
{
    Task<MemberAdmissionRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberAdmissionRecord> UpsertAsync(
        MemberAdmissionRecord admission,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<MemberAdmissionRecord> Items, int TotalCount)> ListPageAsync(
        AdmissionState? state,
        int skip,
        int take,
        CancellationToken cancellationToken);
}
