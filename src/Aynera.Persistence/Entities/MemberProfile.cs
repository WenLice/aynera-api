using Aynera.Domain.Auth.Enums;
using Aynera.Persistence.Common;

namespace Aynera.Persistence.Entities;

public sealed class MemberProfile : ISoftDeletable
{
    public Guid UserId { get; set; }

    /// <summary>
    /// The member's own name, as they write it — a first name or a full name, their choice.
    /// Always kept for verification and for what a match sees after a mutual yes.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional name strangers see before a mutual match. When it is null the member is shown
    /// as the first letter of <see cref="Name"/>.
    /// </summary>
    public string? Nickname { get; set; }

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

    /// <summary>Whole centimetres. Optional — the app's basics step is skippable.</summary>
    public int? HeightCm { get; set; }

    /// <summary>Where the member is from, as free text. Required — the app asks for it on the birth step.</summary>
    public string Hometown { get; set; } = string.Empty;

    /// <summary>What the member does with their days, as free text. Optional.</summary>
    public string? Work { get; set; }

    public string? Religion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
