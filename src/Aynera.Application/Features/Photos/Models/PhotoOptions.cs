namespace Aynera.Application.Features.Photos.Models;

public sealed class PhotoOptions
{
    public const string SectionName = "Aynera:Photos";

    public int MaxCount { get; set; } = 5;

    /// <summary>
    /// Slots that must show the member, matched against their verified face: 1 is "Your face", 2 is
    /// "Full frame". The others ("your world", "something you love") may hold a place, a plate or a
    /// friend; their match is recorded for curators but never blocks the upload.
    /// </summary>
    public int[] MustMatchSlots { get; set; } = [1, 2];
    public int MaxBytes { get; set; } = 5 * 1024 * 1024;
    public int JpegQuality { get; set; } = 85;
    public int MaxDimension { get; set; } = 1920;

    /// <summary>
    /// Stub face-match outcome for local/dev: Pending, Matched, Rejected, or Skipped.
    /// </summary>
    public string StubFaceMatchStatus { get; set; } = "Matched";
}
