using Elaris.Domain.Photos.Enums;
using Elaris.Persistence.Common;

namespace Elaris.Persistence.Entities;

/// <summary>One introduction / liveness video per member (replaced on re-upload).</summary>
public sealed class MemberIntroductionVideo : ISoftDeletable
{
    public Guid UserId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int ByteSize { get; set; }
    public byte[] Data { get; set; } = [];
    public FaceMatchStatus FaceMatchStatus { get; set; } = FaceMatchStatus.Pending;
    public decimal? FaceMatchScore { get; set; }
    public bool GuidelinePassed { get; set; }
    public string? GuidelineDetail { get; set; }
    public string? Transcript { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
