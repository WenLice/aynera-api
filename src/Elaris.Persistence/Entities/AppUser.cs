using Elaris.Domain.Auth.Enums;
using Elaris.Persistence.Common;
using Microsoft.AspNetCore.Identity;

namespace Elaris.Persistence.Entities;

public sealed class AppUser : IdentityUser<Guid>, ISoftDeletable, IActivatable
{
    public AccountKind AccountKind { get; set; } = AccountKind.Member;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>Phone at delete time (audit); login identity fields are renamed to free uniqueness.</summary>
    public string? DeletedPhoneE164 { get; set; }

    public ICollection<RefreshSession> RefreshSessions { get; set; } = new List<RefreshSession>();
    public MemberProfile? Profile { get; set; }
    public ICollection<MemberPhoto> Photos { get; set; } = new List<MemberPhoto>();
    public MemberIntroductionVideo? IntroductionVideo { get; set; }
}
