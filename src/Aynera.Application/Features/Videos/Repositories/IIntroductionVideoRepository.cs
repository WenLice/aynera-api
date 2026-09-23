using Aynera.Domain.Videos.Records;

namespace Aynera.Application.Features.Videos.Repositories;

public interface IIntroductionVideoRepository
{
    /// <summary>
    /// The video's metadata. <c>Data</c> is empty: the bytes live in object storage and are only
    /// fetched by <see cref="ReadContentAsync"/>, so reading a status never downloads the video.
    /// </summary>
    Task<IntroductionVideoRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The video's bytes, or null when the member has none.</summary>
    Task<byte[]?> ReadContentAsync(Guid userId, CancellationToken cancellationToken);

    Task<IntroductionVideoRecord> UpsertAsync(
        IntroductionVideoRecord video,
        CancellationToken cancellationToken);

    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Sets or clears (null) the caption. Throws <c>video_not_found</c> when there is no video.</summary>
    Task UpdateCaptionAsync(Guid userId, string? caption, CancellationToken cancellationToken);
}
