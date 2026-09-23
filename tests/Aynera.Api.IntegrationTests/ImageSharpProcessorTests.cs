using Aynera.Application.Features.Photos.Models;
using Aynera.Infrastructure.Services;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// No database: these exercise the image pipeline on its own. The point is privacy — a phone photo
/// carries where it was taken, and a member's profile photo must not publish their home.
/// </summary>
public sealed class ImageSharpProcessorTests
{
    [Fact]
    public async Task ProcessedPhoto_CarriesNoLocationOrCameraMetadata()
    {
        var input = PhoneJpegWithGps();

        var processor = new ImageSharpProcessor(Options.Create(new PhotoOptions()));
        var processed = await processor.ProcessAsync(new MemoryStream(input), "image/jpeg", CancellationToken.None);

        using var output = Image.Load(processed.Data);
        Assert.Null(output.Metadata.ExifProfile);
        Assert.Null(output.Metadata.XmpProfile);
        Assert.Null(output.Metadata.IptcProfile);
    }

    [Fact]
    public void Fixture_ReallyCarriesGps()
    {
        // Guards the test above: if the fixture lost its GPS, "no metadata" would prove nothing.
        using var image = Image.Load(PhoneJpegWithGps());
        Assert.NotNull(image.Metadata.ExifProfile);
        Assert.True(image.Metadata.ExifProfile!.TryGetValue(ExifTag.GPSLatitude, out _));
    }

    private static byte[] PhoneJpegWithGps()
    {
        using var image = new Image<Rgb24>(64, 64, new Rgb24(200, 120, 90));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(12, 1), new Rational(58, 1), new Rational(19, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(77, 1), new Rational(35, 1), new Rational(40, 1)]);
        exif.SetValue(ExifTag.Make, "PhoneMaker");
        image.Metadata.ExifProfile = exif;

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }
}
