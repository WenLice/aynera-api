namespace Aynera.Application.Features.Photos.Models;

public sealed record PhotoUploadInput(
    Stream Content,
    string? FileName,
    string? ContentType,
    long Length,
    // The slot to put this photo in (1-based); null takes the lowest free one. A slot that already
    // holds a photo is replaced, which is how the app changes one photo without touching the rest.
    int? Slot = null,
    string? Caption = null);
