using Aynera.Domain.Admissions.Requests;
using Aynera.Domain.Admissions.Responses;
using Aynera.Domain.Common;

namespace Aynera.Application.Features.Admissions.Services.Interfaces;

public interface IMemberAdmissionService
{
    Task<MemberAdmissionDto> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberAdmissionDto> SubmitAsync(Guid userId, CancellationToken cancellationToken);

    Task<MemberAdmissionDto> DecideAsync(
        Guid userId,
        AdmissionDecisionRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<MemberAdmissionDto> AcceptConsentAsync(
        Guid userId,
        AcceptConsentRequest request,
        CancellationToken cancellationToken);

    Task<PagedResult<MemberAdmissionSummaryDto>> ListAsync(
        string? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
