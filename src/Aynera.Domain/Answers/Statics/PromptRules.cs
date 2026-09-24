using System.Text.RegularExpressions;
using Aynera.Domain.Answers.Records;

namespace Aynera.Domain.Answers.Statics;

/// <summary>The rules for conversation prompts, shared by validation and registration progress.</summary>
public static partial class PromptRules
{
    /// <summary>Registration asks for this many answered prompts.</summary>
    public const int RequiredAnswered = 2;

    /// <summary>The most prompts a member can hold; the third is added later from the profile editor.</summary>
    public const int MaxPrompts = 3;

    /// <summary>A typed answer is a sentence or two, not an essay.</summary>
    public const int TextMaxLength = 300;

    /// <summary>
    /// Prompt ids become part of an object key (<c>voice_{promptId}.m4a</c>), so they are held to a
    /// strict, path-safe shape rather than the looser key rule the other answers use.
    /// </summary>
    public static bool IsValidPromptId(string? promptId) =>
        !string.IsNullOrEmpty(promptId) && PromptIdPattern().IsMatch(promptId);

    /// <summary>
    /// How many chosen prompts have an answer — typed, recorded, or both. A recording for a prompt
    /// the member no longer holds does not count.
    /// </summary>
    public static int AnsweredCount(
        IReadOnlyList<MemberPromptAnswer> prompts,
        IReadOnlyCollection<string> recordedPromptIds) =>
        prompts.Count(p =>
            !string.IsNullOrWhiteSpace(p.Text)
            || recordedPromptIds.Contains(p.PromptId, StringComparer.Ordinal));

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,31}$")]
    private static partial Regex PromptIdPattern();
}
