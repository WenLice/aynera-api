namespace Aynera.Application.Features.Voice.Models;

/// <summary>Bounds for spoken prompt answers (<c>Aynera:VoiceAnswers</c>).</summary>
public sealed class VoiceAnswerOptions
{
    public const string SectionName = "Aynera:VoiceAnswers";

    /// <summary>
    /// The recording limit the app enforces. The server cannot read a duration without decoding the
    /// file, so it holds the line with <see cref="MaxBytes"/> instead; this is published for clients.
    /// </summary>
    public int MaxSeconds { get; set; } = 60;

    /// <summary>
    /// A minute of speech is roughly 0.5–1 MB as AAC or Opus. Five times that leaves room for a
    /// high-bitrate phone recording while refusing anything that is clearly not a voice note.
    /// </summary>
    public int MaxBytes { get; set; } = 5 * 1024 * 1024;
}
