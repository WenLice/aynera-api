namespace Aynera.Domain.Answers.Records;

/// <summary>
/// A member's answers to the everyday, belief and vibe questions, their conversation prompts and
/// the optional extras from the profile editor.
/// <para>
/// One field per category rather than one document, because the categories genuinely hold
/// different shapes: everyday and beliefs are one answer per question with its own visibility,
/// vibe is a flat set of chips, prompts are an ordered list. Splitting them also makes a key
/// collision impossible — <c>movement</c> is both an everyday question and a vibe group, and
/// <c>family</c> is both a belief and a rhythm question, and they cannot meet.
/// </para>
/// <para>
/// Every question is optional, so an empty category is a normal state and not a missing answer.
/// </para>
/// </summary>
/// <param name="Vibe">Chip keys, as a set. Order carries no meaning.</param>
/// <param name="Prompts">The chosen conversation prompts, in the order the member picked them.</param>
/// <param name="Dealbreaker">"Something I don't usually say first" — free text, profile editor only.</param>
/// <param name="Rhythm">The shape of an ordinary week, keyed by question (<c>socialEnergy</c>, <c>weekends</c>, <c>family</c>).</param>
public sealed record MemberProfileAnswersRecord(
    Guid UserId,
    IReadOnlyDictionary<string, MemberAnswer> Lifestyle,
    IReadOnlyDictionary<string, MemberAnswer> Beliefs,
    IReadOnlyList<string> Vibe,
    IReadOnlyList<MemberPromptAnswer> Prompts,
    string? Dealbreaker,
    IReadOnlyDictionary<string, string> Rhythm)
{
    public static MemberProfileAnswersRecord Empty(Guid userId) =>
        new(
            userId,
            new Dictionary<string, MemberAnswer>(),
            new Dictionary<string, MemberAnswer>(),
            [],
            [],
            null,
            new Dictionary<string, string>());

    /// <summary>True when the member has answered nothing at all, in any category.</summary>
    public bool IsEmpty =>
        Lifestyle.Count == 0
        && Beliefs.Count == 0
        && Vibe.Count == 0
        && Prompts.Count == 0
        && Dealbreaker is null
        && Rhythm.Count == 0;
}
