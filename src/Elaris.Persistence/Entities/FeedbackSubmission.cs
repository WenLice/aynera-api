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
    /// <summary>True when the submitted email matches an existing (non-deleted) member account.</summary>
    public bool IsExistingUser { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
