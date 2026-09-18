using Aynera.Domain.Venues.Enums;
using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

public sealed class Venue : ISoftDeletable, IActivatable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public VenueType Type { get; set; }

    /// <summary>References <see cref="EarlyAccessCity"/>.Id (validated in the service; not a database FK).</summary>
    public Guid CityId { get; set; }

    /// <summary>Member-visible locality (e.g. "Hauz Khas").</summary>
    public string Area { get; set; } = string.Empty;

    /// <summary>Full street address. Admin-only; never returned to members.</summary>
    public string Address { get; set; } = string.Empty;

    public List<string> PhotoUrls { get; set; } = [];

    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhoneE164 { get; set; } = string.Empty;

    public int? Capacity { get; set; }
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
