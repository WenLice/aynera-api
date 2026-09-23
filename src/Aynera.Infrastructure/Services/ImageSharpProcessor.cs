using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Domain.Photos.Exceptions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Aynera.Infrastructure.Services;

public sealed class ImageSharpProcessor : IImageProcessor
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    private readonly PhotoOptions _options;

    public ImageSharpProcessor(IOptions<PhotoOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ProcessedImage> ProcessAsync(
        Stream input,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && !AllowedContentTypes.Contains(contentType.Split(';', 2)[0].Trim()))
        {
            throw new PhotoException(
                "photo_unsupported_type",
                "Only JPEG, PNG, and WebP images are allowed.");
        }

        try
        {
            using var image = await Image.LoadAsync(input, cancellationToken);

            // Phones record rotation in EXIF rather than in the pixels. Bake it in first, because the
            // next step throws EXIF away and the photo would otherwise show sideways.
            image.Mutate(ctx => ctx.AutoOrient());

            // A phone photo carries where and when it was taken and on what device. None of that may
            // reach another member — a home-screen selfie would publish the member's address.
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;
            // The colour profile stays: it holds no location, and dropping it washes out wide-gamut
            // phone photos.

            var max = Math.Max(1, _options.MaxDimension);
            if (image.Width > max || image.Height > max)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(max, max)
                }));
            }

            await using var output = new MemoryStream();
            var encoder = new JpegEncoder { Quality = Math.Clamp(_options.JpegQuality, 40, 95) };
            await image.SaveAsJpegAsync(output, encoder, cancellationToken);
            var bytes = output.ToArray();
            return new ProcessedImage(bytes, "image/jpeg", bytes.Length);
        }
        catch (PhotoException)
        {
            throw;
        }
        catch (UnknownImageFormatException)
        {
            throw new PhotoException(
                "photo_unsupported_type",
                "Only JPEG, PNG, and WebP images are allowed.");
        }
        catch (Exception)
        {
            throw new PhotoException("photo_invalid", "Could not read the uploaded image.");
        }
    }
}
