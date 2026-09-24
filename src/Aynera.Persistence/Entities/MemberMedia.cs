using Aynera.Persistence.Common;
using Aynera.Domain.Media.Enums;
using Aynera.Domain.Photos.Enums;

namespace Aynera.Persistence.Entities;

/// <summary>
/// One stored photo or video. The bytes live in object storage under <see cref="StorageKey"/>;
/// this row holds only where to find them and what the checks concluded.
/// <para>
/// Photos, the introduction video and the liveness video share the table. Video-only columns
/// (transcript, guideline) stay null on photos, and photo-only ones (<see cref="Index"/>,
/// <see cref="IsReference"/>) stay null or false on videos.
/// </para>
/// </summary>
public sealed class MemberMedia : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public MediaKind Kind { get; set; }

    /// <summary>The photo slot, 1-based — the same number as the <c>photo_N</c> file name. Null for videos.</summary>
    public int? Index { get; set; }

    /// <summary>
    /// The prompt a <c>VoiceAnswer</c> answers — its key, e.g. <c>know</c>. Null for every other kind.
    /// </summary>
    public string? PromptId { get; set; }

    /// <summary>The object key in the bucket, e.g. <c>{userId}/photo_2.jpg</c>. Never a URL.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    /// <summary>The line the member wrote to go with this photo or video.</summary>
    public string? Caption { get; set; }
    public int ByteSize { get; set; }

    /// <summary>The photo other photos and the videos are face-matched against.</summary>
    public bool IsReference { get; set; }

    public FaceMatchStatus FaceMatchStatus { get; set; } = FaceMatchStatus.Pending;
    public decimal? FaceMatchScore { get; set; }
    public bool? GuidelinePassed { get; set; }
    public string? GuidelineDetail { get; set; }
    public string? Transcript { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public AppUser User { get; set; } = null!;
}
