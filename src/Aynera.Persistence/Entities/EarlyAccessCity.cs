using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

public sealed class EarlyAccessCity : ISoftDeletable, IActivatable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Wave { get; set; } = 1;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
