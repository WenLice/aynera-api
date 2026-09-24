namespace Aynera.Application.Features.Voice.Models;

public sealed record VoiceUploadInput(
    Stream Content,
    string? ContentType,
    long Length,
    string? PromptId);
