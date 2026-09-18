namespace Aynera.Domain.Admissions.Responses;

public sealed record MemberAdmissionDto(
    Guid UserId,
    string State,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    Guid? DecidedByUserId,
    string? DecisionReason,
    string? ReviewNote,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<MemberConsentDto> Consents,
    MemberEligibilityDto Eligibility);
