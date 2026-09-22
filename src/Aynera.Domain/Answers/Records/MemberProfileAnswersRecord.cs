using Aynera.Domain.Answers.Records;

namespace Aynera.Domain.Answers.Records;

/// <summary>
/// A member's answers to the everyday, belief and vibe questions — pages 23 to 34 of registration.
/// <para>
/// One field per category rather than one document, because the three genuinely hold different
/// shapes: everyday and beliefs are one answer per question with its own visibility, while vibe is
/// a flat set of chips. Splitting them also makes a key collision impossible — <c>movement</c> is
/// both an everyday question and a vibe group, and they cannot meet.
/// </para>
/// <para>
/// Every question is optional, so an empty category is a normal state and not a missing answer.
/// </para>
/// </summary>
/// <param name="Vibe">Chip keys, as a set. Order carries no meaning.</param>
public sealed record MemberProfileAnswersRecord(
    Guid UserId,
    IReadOnlyDictionary<string, MemberAnswer> Lifestyle,
    IReadOnlyDictionary<string, MemberAnswer> Beliefs,
    IReadOnlyList<string> Vibe)
{
    public static MemberProfileAnswersRecord Empty(Guid userId) =>
        new(userId, new Dictionary<string, MemberAnswer>(), new Dictionary<string, MemberAnswer>(), []);

    /// <summary>True when the member has answered nothing at all, in any category.</summary>
    public bool IsEmpty => Lifestyle.Count == 0 && Beliefs.Count == 0 && Vibe.Count == 0;
}
