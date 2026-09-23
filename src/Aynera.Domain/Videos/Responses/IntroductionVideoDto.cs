namespace Aynera.Domain.Videos.Responses;

public sealed record IntroductionVideoDto(
    Guid UserId,
    string ContentType,
    int ByteSize,
    string FaceMatchStatus,
    decimal? FaceMatchScore,
    bool GuidelinePassed,
    string? GuidelineDetail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? Caption = null,
    // A short-lived signed link for playback; ask again rather than storing it.
    string? Url = null);
