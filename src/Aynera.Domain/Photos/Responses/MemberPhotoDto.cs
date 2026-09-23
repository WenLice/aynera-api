namespace Aynera.Domain.Photos.Responses;

public sealed record MemberPhotoDto(
    Guid Id,
    int SortOrder,
    string ContentType,
    int ByteSize,
    bool IsReference,
    string FaceMatchStatus,
    decimal? FaceMatchScore,
    DateTimeOffset CreatedAtUtc,
    string? Caption = null,
    // A short-lived signed link to the image. The bucket is private, so this is the only way a
    // client can show it; ask again rather than storing it.
    string? Url = null);
