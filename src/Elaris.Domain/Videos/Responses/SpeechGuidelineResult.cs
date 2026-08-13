namespace Elaris.Domain.Videos.Responses;

public sealed record SpeechGuidelineResult(
    bool Passed,
    string? Detail,
    IReadOnlyList<string> MatchedBannedWords);
