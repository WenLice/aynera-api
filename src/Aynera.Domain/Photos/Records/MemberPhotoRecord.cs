namespace Aynera.Domain.Photos.Records;

public sealed record MemberPhotoRecord(
    Guid Id,
    Guid UserId,
    int SortOrder,
    string ContentType,
    int ByteSize,
    byte[] Data,
    bool IsReference,
    string FaceMatchStatus,
    decimal? FaceMatchScore,
    DateTimeOffset CreatedAtUtc,
    string? Caption = null,
    string? StorageKey = null);
