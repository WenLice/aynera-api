using Aynera.Application.Features.Videos.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Aynera.Infrastructure.Services;

/// <summary>
/// Dev stub: does not decode video. Returns a generated JPEG frame for face-match plumbing.
/// Replace with FFmpeg (or similar) frame extraction in production.
/// </summary>
public sealed class StubVideoFrameExtractor : IVideoFrameExtractor
{
    public async Task<byte[]> ExtractFaceFrameJpegAsync(
        Stream video,
        string contentType,
        CancellationToken cancellationToken)
    {
        // Consume stream so callers can rely on position semantics when reusing buffers.
        if (video.CanSeek)
        {
            video.Position = 0;
        }

        var buffer = new byte[8192];
        while (await video.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken) > 0)
        {
            // discard
        }

        using var image = new Image<Rgb24>(64, 64);
        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 80 }, cancellationToken);
        return output.ToArray();
    }
}
