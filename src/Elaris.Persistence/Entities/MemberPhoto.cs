using Elaris.Domain.Photos.Enums;
using Elaris.Persistence.Common;

namespace Elaris.Persistence.Entities;

public sealed class MemberPhoto : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int SortOrder { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int ByteSize { get; set; }
    public byte[] Data { get; set; } = [];
    public bool IsReference { get; set; }
    public FaceMatchStatus FaceMatchStatus { get; set; } = FaceMatchStatus.Pending;
    public decimal? FaceMatchScore { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
