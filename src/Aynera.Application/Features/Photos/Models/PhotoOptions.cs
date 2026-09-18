namespace Aynera.Application.Features.Photos.Models;

public sealed class PhotoOptions
{
    public const string SectionName = "Aynera:Photos";

    public int MaxCount { get; set; } = 6;
    public int MaxBytes { get; set; } = 5 * 1024 * 1024;
    public int JpegQuality { get; set; } = 85;
    public int MaxDimension { get; set; } = 1920;

    /// <summary>
    /// Stub face-match outcome for local/dev: Pending, Matched, Rejected, or Skipped.
    /// </summary>
    public string StubFaceMatchStatus { get; set; } = "Matched";
}
