namespace Aynera.Domain.Answers.Records;

/// <summary>
/// One answer to one catalogued question, plus whether the member publishes it.
/// <para>
/// Answering and publishing are separate decisions — the app says so on the page itself — so the
/// flag travels with the answer rather than sitting on the category. A curator reads every answer;
/// only the public ones reach another member.
/// </para>
/// </summary>
/// <param name="Option">
/// The option key, not its label. Relabelling "Non-vegetarian" must not orphan the answers already
/// given, so what is stored is the identity, not the wording.
/// </param>
public sealed record MemberAnswer(string Option, bool Public = true);
