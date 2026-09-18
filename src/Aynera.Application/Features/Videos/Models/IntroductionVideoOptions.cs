namespace Aynera.Application.Features.Videos.Models;

public sealed class IntroductionVideoOptions
{
    public const string SectionName = "Aynera:IntroductionVideo";

    public int MaxBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Dev/test transcript used when real speech-to-text is not wired.
    /// Empty means no words detected (guideline check passes if no banned hits).
    /// </summary>
    public string StubTranscript { get; set; } = string.Empty;

    /// <summary>Community-guidelines banned words/phrases (case-insensitive substring match).</summary>
    public string[] BannedWords { get; set; } =
    [
        "kill",
        "suicide",
        "terrorist",
        "nude",
        "porn",
        "rape",
        "molest"
    ];
}
