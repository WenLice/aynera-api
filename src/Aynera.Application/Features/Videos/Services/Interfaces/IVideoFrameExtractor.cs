namespace Aynera.Application.Features.Videos.Services.Interfaces;

public interface IVideoFrameExtractor
{
    /// <summary>Extracts a representative frame as JPEG bytes for face matching.</summary>
    Task<byte[]> ExtractFaceFrameJpegAsync(
        Stream video,
        string contentType,
        CancellationToken cancellationToken);
}
