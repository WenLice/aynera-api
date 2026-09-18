namespace Aynera.Domain.Photos.Responses;

public sealed record MemberPhotoDto(
    Guid Id,
    int SortOrder,
    string ContentType,
    int ByteSize,
    bool IsReference,
    string FaceMatchStatus,
    decimal? FaceMatchScore,
    DateTimeOffset CreatedAtUtc);
