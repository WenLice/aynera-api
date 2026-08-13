using Elaris.Persistence.Common;

namespace Elaris.Persistence.Entities;

/// <summary>Product idea submissions from the marketing suggestion box.</summary>
public sealed class Suggestion : ISoftDeletable, IActivatable
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
