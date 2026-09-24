namespace Aynera.Domain.Voice.Responses;

/// <summary>A stored spoken answer.</summary>
/// <param name="Url">A signed playback link, valid for an hour. Null only if storage cannot sign one.</param>
public sealed record VoiceAnswerDto(
    string PromptId,
    string ContentType,
    int ByteSize,
    string? Url,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
