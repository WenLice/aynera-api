namespace Aynera.Domain.Admissions.Responses;

/// <summary>
/// Review-queue row. Carries no eligibility verdict on purpose: computing one per row would query
/// account, profile, consent and identity evidence for every member in the page.
/// </summary>
public sealed record MemberAdmissionSummaryDto(
    Guid UserId,
    string State,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    Guid? DecidedByUserId,
    DateTimeOffset CreatedAtUtc);
