namespace Aynera.Domain.Answers.Responses;

/// <summary>A chosen prompt, its typed answer if any, and whether a recording is stored for it.</summary>
/// <param name="HasAudio">True when a spoken answer is stored; fetch it from <c>voice-answers/GetAll</c>.</param>
public sealed record PromptAnswerDto(string PromptId, string? Text, bool HasAudio);
