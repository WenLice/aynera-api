namespace Elaris.Application.Features.Videos.Models;

public sealed record VideoUploadInput(
    Stream Content,
    string? FileName,
    string? ContentType,
    long Length);
