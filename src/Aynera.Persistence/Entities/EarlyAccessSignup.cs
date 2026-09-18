using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

public sealed class EarlyAccessSignup : ISoftDeletable, IActivatable
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string City { get; set; } = string.Empty;
    public string Interest { get; set; } = string.Empty;
    public string? Intent { get; set; }
    public string? MeetPreference { get; set; }
    public bool IsAdult { get; set; }
    public bool MarketingConsent { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
