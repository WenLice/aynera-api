namespace Aynera.Domain.Media.Requests;

/// <summary>Sets a photo's or video's caption. Null or blank clears it.</summary>
public sealed record UpdateMediaCaptionRequest(string? Caption);
