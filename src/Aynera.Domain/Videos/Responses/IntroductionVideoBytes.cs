namespace Aynera.Domain.Videos.Responses;

public sealed record IntroductionVideoBytes(
    Guid UserId,
    string ContentType,
    byte[] Data);
