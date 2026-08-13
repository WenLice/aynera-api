namespace Elaris.Application.Features.Photos.Models;

public sealed record ProcessedImage(byte[] Data, string ContentType, int ByteSize);
