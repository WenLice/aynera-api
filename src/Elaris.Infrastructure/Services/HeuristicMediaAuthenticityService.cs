using System.Text;
using Elaris.Application.Features.Media.Models;
using Elaris.Application.Features.Media.Services.Interfaces;
using Elaris.Domain.Media.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;

namespace Elaris.Infrastructure.Services;

/// <summary>
/// Free/heuristic authenticity checks: EXIF/XMP/IPTC and embedded text markers for known AI tools.
/// Not a substitute for a dedicated deepfake/AI-image provider — swap this implementation when one is available.
/// </summary>
public sealed class HeuristicMediaAuthenticityService : IMediaAuthenticityService
{
    private readonly MediaAuthenticityOptions _options;
    private readonly ILogger<HeuristicMediaAuthenticityService> _logger;

    public HeuristicMediaAuthenticityService(
        IOptions<MediaAuthenticityOptions> options,
        ILogger<HeuristicMediaAuthenticityService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MediaAuthenticityResult> AssessImageAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (_options.StubForceAiDetected)
        {
            return Reject("Dev stub forced AI detection.", "stub");
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        try
        {
            using var image = await Image.LoadAsync(content, cancellationToken);
            var haystack = new StringBuilder();

            AppendExif(image.Metadata.ExifProfile, haystack);
            AppendIptc(image.Metadata.IptcProfile, haystack);
            AppendXmp(image.Metadata.XmpProfile, haystack);

            var hit = FindMarker(haystack.ToString());
            if (hit is not null)
            {
                _logger.LogWarning("Image authenticity failed: marker {Marker}", hit);
                return Reject(
                    "This photo appears to be AI-generated and cannot be used for identity verification.",
                    "metadata_heuristic");
            }

            return Pass("metadata_heuristic");
        }
        catch (UnknownImageFormatException)
        {
            return _options.AllowWhenUndetermined
                ? Pass("undetermined_format")
                : Reject("Could not verify photo authenticity.", "undetermined_format");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image authenticity assessment failed");
            return _options.AllowWhenUndetermined
                ? Pass("undetermined_error")
                : Reject("Could not verify photo authenticity.", "undetermined_error");
        }
        finally
        {
            if (content.CanSeek)
            {
                content.Position = 0;
            }
        }
    }

    public async Task<MediaAuthenticityResult> AssessVideoAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (_options.StubForceAiDetected)
        {
            return Reject("Dev stub forced AI detection.", "stub");
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        // Sample leading bytes for ASCII tool markers (weak free signal until a real detector is wired).
        var sample = new byte[Math.Min(512 * 1024, content.CanSeek ? (int)Math.Min(content.Length, 512 * 1024) : 512 * 1024)];
        var read = await content.ReadAsync(sample.AsMemory(0, sample.Length), cancellationToken);
        var text = Encoding.ASCII.GetString(sample, 0, read);

        var hit = FindMarker(text);
        if (hit is not null)
        {
            _logger.LogWarning("Video authenticity failed: marker {Marker}", hit);
            return Reject(
                "This video appears to be AI-generated and cannot be used for identity verification.",
                "embedded_text_heuristic");
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        return _options.AllowWhenUndetermined
            ? Pass("video_heuristic_undetermined")
            : Reject("Could not verify video authenticity.", "video_heuristic_undetermined");
    }

    private string? FindMarker(string haystack)
    {
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return null;
        }

        foreach (var marker in _options.AiToolMarkers ?? [])
        {
            if (string.IsNullOrWhiteSpace(marker))
            {
                continue;
            }

            if (haystack.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return marker;
            }
        }

        return null;
    }

    private static void AppendExif(ExifProfile? profile, StringBuilder sb)
    {
        if (profile is null)
        {
            return;
        }

        foreach (var value in profile.Values)
        {
            try
            {
                sb.Append(' ').Append(value.GetValue());
            }
            catch
            {
                // ignore unreadable tags
            }
        }
    }

    private static void AppendIptc(IptcProfile? profile, StringBuilder sb)
    {
        if (profile?.Values is null)
        {
            return;
        }

        foreach (var value in profile.Values)
        {
            sb.Append(' ').Append(value.Value);
        }
    }

    private static void AppendXmp(XmpProfile? profile, StringBuilder sb)
    {
        if (profile is null)
        {
            return;
        }

        try
        {
            var data = profile.ToByteArray();
            if (data is { Length: > 0 })
            {
                sb.Append(' ').Append(Encoding.UTF8.GetString(data));
            }
        }
        catch
        {
            // ignore
        }
    }

    private static MediaAuthenticityResult Pass(string detector) =>
        new(true, null, detector);

    private static MediaAuthenticityResult Reject(string detail, string detector) =>
        new(false, detail, detector);
}
