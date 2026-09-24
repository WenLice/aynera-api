namespace Aynera.Domain.Voice.Records;

/// <summary>
/// A spoken answer to one conversation prompt. The bytes live in object storage at
/// <c>{userId}/voice_{promptId}.{ext}</c>; <see cref="Data"/> is empty on metadata reads.
/// </summary>
public sealed record VoiceAnswerRecord(
    Guid UserId,
    string PromptId,
    string ContentType,
    int ByteSize,
    byte[] Data,
    bool GuidelinePassed,
    string? GuidelineDetail,
    string? Transcript,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? StorageKey = null);
