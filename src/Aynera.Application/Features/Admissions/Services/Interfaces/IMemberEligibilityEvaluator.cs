using Aynera.Domain.Admissions.Responses;

namespace Aynera.Application.Features.Admissions.Services.Interfaces;

/// <summary>
/// The single place match eligibility is decided. Matching, introductions and date planning must
/// depend on this rather than reading admission state directly, so no caller can approximate the
/// rule and drift from it.
/// </summary>
public interface IMemberEligibilityEvaluator
{
    Task<MemberEligibilityDto> EvaluateAsync(Guid userId, CancellationToken cancellationToken);
}
