namespace Elaris.Domain.Photos.Responses;

public sealed record MemberPhotoBytes(
    Guid Id,
    string ContentType,
    byte[] Data);
