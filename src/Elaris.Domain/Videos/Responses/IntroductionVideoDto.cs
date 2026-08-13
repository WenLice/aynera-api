namespace Elaris.Domain.Videos.Responses;

public sealed record IntroductionVideoDto(
    Guid UserId,
    string ContentType,
    int ByteSize,
    string FaceMatchStatus,
    decimal? FaceMatchScore,
    bool GuidelinePassed,
    string? GuidelineDetail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
