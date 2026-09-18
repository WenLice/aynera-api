namespace Aynera.Domain.Videos.Records;

public sealed record IntroductionVideoRecord(
    Guid UserId,
    string ContentType,
    int ByteSize,
    byte[] Data,
    string FaceMatchStatus,
    decimal? FaceMatchScore,
    bool GuidelinePassed,
    string? GuidelineDetail,
    string? Transcript,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
