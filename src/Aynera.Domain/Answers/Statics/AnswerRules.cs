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

    /// <summary>"Something I don't usually say first" — a short paragraph at most.</summary>
    public const int DealbreakerMaxLength = 500;

    /// <summary>
    /// "Prefer not to say" — stored as the answer, like any other option, but never published, so
    /// it has no visibility setting: its <c>public</c> flag is not written to member settings.
    /// </summary>
    public static bool IsDeclineOption(string? option) =>
        string.Equals(option?.Trim(), "Prefer not to say", StringComparison.OrdinalIgnoreCase);
}
