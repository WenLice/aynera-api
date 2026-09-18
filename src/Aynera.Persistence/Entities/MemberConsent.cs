using Aynera.Domain.Admissions.Enums;

namespace Aynera.Persistence.Entities;

/// <summary>
/// A member's acceptance of one policy document at one version. Rows are append-only: a version
/// bump adds a new row rather than overwriting, so the acceptance history stays auditable and an
/// un-accepted new version simply stops satisfying eligibility.
/// </summary>
public sealed class MemberConsent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ConsentPolicyKind PolicyKind { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset AcceptedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public AppUser User { get; set; } = null!;
}
