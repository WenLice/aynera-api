using Aynera.Domain.Auth.Enums;
using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

public sealed class MemberProfile : ISoftDeletable
{
    public Guid UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public DateOnly DateOfBirth { get; set; }
    /// <summary>Canonical catalog name of the member's city (from <see cref="EarlyAccessCity"/>).</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// The member's city in the shared city catalog (<see cref="EarlyAccessCity.Id"/>), the same key
    /// <see cref="Venue.CityId"/> uses. Required: every member belongs to exactly one catalog city.
    /// No database FK, matching the venue convention.
    /// </summary>
    public Guid CityId { get; set; }
    public string? Religion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
