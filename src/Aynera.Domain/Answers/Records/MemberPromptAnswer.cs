namespace Aynera.Domain.Answers.Records;

/// <summary>
/// One conversation prompt the member chose, and their written answer to it if they typed one.
/// <para>
/// A prompt can be answered by typing, by recording, or both: the recording lives in object
/// storage as a <c>VoiceAnswer</c> media row keyed by the same <paramref name="PromptId"/>, so this
/// record only ever holds the text.
/// </para>
/// </summary>
/// <param name="PromptId">The prompt's key from the app's list (e.g. <c>know</c>), never its wording.</param>
/// <param name="Text">The typed answer; null when the member only recorded one.</param>
public sealed record MemberPromptAnswer(string PromptId, string? Text = null);
