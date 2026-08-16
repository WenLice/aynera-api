using Elaris.Persistence.Common;

namespace Elaris.Persistence.Entities;

/// <summary>Grievance / feedback submissions from the marketing grievance channel.</summary>
public sealed class FeedbackSubmission : ISoftDeletable, IActivatable
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>True when <see cref="MemberId"/> was resolved at submit time.</summary>
    public bool IsExistingUser { get; set; }
    /// <summary>Matched member account id when the submit email belongs to a member; otherwise null.</summary>
    public Guid? MemberId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
