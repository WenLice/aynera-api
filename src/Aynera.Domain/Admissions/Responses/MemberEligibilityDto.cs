namespace Aynera.Domain.Admissions.Responses;

/// <summary>
/// Computed match-eligibility verdict. Never persisted as a cached flag: it is recomputed from
/// current admission, account, profile, consent and identity evidence on every read so that a
/// later state change (deactivation, restriction, policy-version bump) takes effect immediately.
/// </summary>
/// <param name="UnmetRequirements">
/// Stable reason codes for everything currently blocking eligibility; empty when eligible.
/// </param>
public sealed record MemberEligibilityDto(
    Guid UserId,
    bool IsEligible,
    string AdmissionState,
    IReadOnlyList<string> UnmetRequirements);
