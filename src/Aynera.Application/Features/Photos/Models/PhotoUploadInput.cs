namespace Aynera.Application.Features.Photos.Models;

public sealed record PhotoUploadInput(
    Stream Content,
    string? FileName,
    string? ContentType,
    long Length);
