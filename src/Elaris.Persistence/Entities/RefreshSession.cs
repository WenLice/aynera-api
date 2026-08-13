using Elaris.Persistence.Common;

namespace Elaris.Persistence.Entities;

public sealed class RefreshSession : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Audience { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }
    public string? DeviceLabel { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? ReplacedAtUtc { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
