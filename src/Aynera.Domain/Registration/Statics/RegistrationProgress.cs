using Aynera.Domain.Answers.Records;
using Aynera.Domain.Registration.Records;

namespace Aynera.Domain.Registration.Statics;

/// <summary>
/// The ordered registration steps and which of them a member has satisfied.
/// <para>
/// This is the single place the "where do I resume?" rule lives. The app must not carry its own
/// copy: if it did, the server and the client could disagree about whether a member is finished,
/// which is the failure the server-side draft exists to prevent.
/// </para>
/// </summary>
public static class RegistrationProgress
{
    public const string Phone = "phone";
    public const string Email = "email";
    public const string You = "you";
    public const string Self = "self";
    public const string Birth = "birth";
    public const string Life = "life";
    public const string Looking = "looking";
    public const string Intent = "intent";
    public const string Lifestyle = "lifestyle";
    public const string Beliefs = "beliefs";
    public const string Vibe = "vibe";

    /// <summary>Registration order, matching the app's flow. Later steps append here.</summary>
    public static readonly IReadOnlyList<string> Ordered =
        [Phone, Email, You, Self, Birth, Life, Looking, Intent, Lifestyle, Beliefs, Vibe];

    /// <summary>
    /// The answers a profile cannot be created without. Presence is only ever checked here and at
    /// promotion — never on an individual page write.
    /// </summary>
    public static bool IsProfileComplete(RegistrationAnswers answers) =>
        !string.IsNullOrWhiteSpace(answers.Name)
        && !string.IsNullOrWhiteSpace(answers.Gender)
        && answers.DateOfBirth is not null
        && !string.IsNullOrWhiteSpace(answers.City)
        && !string.IsNullOrWhiteSpace(answers.Hometown);

    /// <summary>
    /// The answers a preferences row cannot be created without — the §6 hard filters.
    /// <para>
    /// <c>MaxAge</c> is deliberately absent: null there means an open upper end, not a missing
    /// answer, so requiring it would make "and older" impossible to express.
    /// </para>
    /// </summary>
    public static bool IsPreferencesComplete(RegistrationAnswers answers) =>
        !string.IsNullOrWhiteSpace(answers.InterestedIn)
        && answers.MinAge is not null
        && !string.IsNullOrWhiteSpace(answers.Track)
        && !string.IsNullOrWhiteSpace(answers.Outcome);

    /// <summary>
    /// Which steps are done, given the account's verification state and the answers so far.
    /// Absent evidence never counts as satisfied, matching the eligibility evaluator's contract.
    /// </summary>
    /// <param name="profileAnswers">
    /// The everyday, belief and vibe answers. Every question in them is optional, so a category
    /// counts as done once it holds anything at all — there is no other way to tell "answered
    /// nothing" apart from "not reached yet". A member who genuinely wants to answer none of a
    /// category is therefore asked again on resume, which is the one rough edge of this rule.
    /// </param>
    public static IReadOnlyList<string> Completed(
        bool phoneConfirmed,
        bool emailConfirmed,
        RegistrationAnswers answers,
        MemberProfileAnswersRecord? profileAnswers = null)
    {
        var done = new List<string>(Ordered.Count);

        if (phoneConfirmed) done.Add(Phone);
        if (emailConfirmed) done.Add(Email);
        if (!string.IsNullOrWhiteSpace(answers.Name)) done.Add(You);
        if (!string.IsNullOrWhiteSpace(answers.Gender)) done.Add(Self);

        // The app collects both on one page, so neither alone finishes the step.
        if (answers.DateOfBirth is not null && !string.IsNullOrWhiteSpace(answers.Hometown)) done.Add(Birth);

        if (!string.IsNullOrWhiteSpace(answers.City)) done.Add(Life);

        // The age range always carries a value from the slider's defaults, so who the member wants
        // to meet is the only answer that marks this page as done.
        if (!string.IsNullOrWhiteSpace(answers.InterestedIn)) done.Add(Looking);

        // Two-stage on one page: a track without an outcome is half an answer.
        if (!string.IsNullOrWhiteSpace(answers.Track) && !string.IsNullOrWhiteSpace(answers.Outcome))
        {
            done.Add(Intent);
        }

        if (profileAnswers is not null)
        {
            if (profileAnswers.Lifestyle.Count > 0) done.Add(Lifestyle);
            if (profileAnswers.Beliefs.Count > 0) done.Add(Beliefs);
            if (profileAnswers.Vibe.Count > 0) done.Add(Vibe);
        }

        return done;
    }

    /// <summary>
    /// The first step in <see cref="Ordered"/> that is not satisfied, or null when all are — which
    /// is where the app reopens the flow instead of restarting it.
    /// </summary>
    public static string? NextStep(IReadOnlyList<string> completed) =>
        Ordered.FirstOrDefault(step => !completed.Contains(step));
}
