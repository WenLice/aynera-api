using Aynera.Domain.Admissions.Enums;

namespace Aynera.Domain.Admissions.Records;

/// <summary>Application-facing admission projection (one per member).</summary>
public sealed record MemberAdmissionRecord(
    Guid UserId,
    AdmissionState State,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    Guid? DecidedByUserId,
    string? DecisionReason,
    string? ReviewNote,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
