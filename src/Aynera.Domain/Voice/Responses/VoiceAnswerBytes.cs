namespace Aynera.Domain.Voice.Responses;

/// <summary>A spoken answer's bytes, for streaming back to a member or a curator.</summary>
public sealed record VoiceAnswerBytes(Guid UserId, string PromptId, string ContentType, byte[] Data);
