using Aynera.Domain.Admissions.Enums;

namespace Aynera.Persistence.Entities;

/// <summary>
/// One admission review per member, keyed by user id like <see cref="MemberProfile"/>. Holds the
/// staff decision only; match eligibility is computed from this plus live account, profile,
/// consent and identity evidence, and is deliberately not cached here.
/// </summary>
public sealed class MemberAdmission
{
    public Guid UserId { get; set; }
    public AdmissionState State { get; set; } = AdmissionState.Draft;
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
