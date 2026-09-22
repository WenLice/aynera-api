namespace Aynera.Domain.Answers.Statics;

/// <summary>Bounds for what a member may send as an answer.</summary>
public static class AnswerRules
{
    /// <summary>Long enough for any catalogued key, short enough that a stray payload is refused.</summary>
    public const int KeyMaxLength = 64;

    /// <summary>
    /// A ceiling on the vibe set. The app walks eight groups and caps the member well below this;
    /// the limit exists so a client that ignores its own cap cannot write an unbounded document.
    /// </summary>
    public const int VibeMaxChips = 100;

    /// <summary>Same reasoning, for the number of questions a category may carry.</summary>
    public const int CategoryMaxAnswers = 50;
}
