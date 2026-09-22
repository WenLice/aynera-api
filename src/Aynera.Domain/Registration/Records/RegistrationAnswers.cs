namespace Aynera.Domain.Registration.Records;

/// <summary>
/// The answers collected across the app's registration pages, as far as the member has got. Every
/// field is nullable because this is an answer sheet in progress, not a profile: the draft is
/// permissive about what is <em>missing</em> and strict about what is <em>wrong</em>.
/// <para>
/// Stored as JSON, so adding a question costs no migration. Enums are held as strings for the same
/// reason <c>MemberProfileRecord</c> does — a value the enum no longer knows round-trips and
/// surfaces at promotion instead of throwing on read.
/// </para>
/// <para>
/// It carries two groups that promote to two different tables and at two different moments: the
/// personal details become a <c>MemberProfile</c>, and the matching preferences a
/// <c>MemberPreferences</c>. Neither waits for the other.
/// </para>
/// </summary>
/// <param name="MaxAge">
/// Null is an open upper end — "<c>MinAge</c> and older" — exactly as the preferences API treats
/// it, not a missing answer. It is therefore never part of the completeness check.
/// </param>
public sealed record RegistrationAnswers(
    // Personal details — pages 9 to 16.
    string? Name = null,
    string? Nickname = null,
    string? Gender = null,
    bool? GenderIsPublic = null,
    DateOnly? DateOfBirth = null,
    int? HeightCm = null,
    string? Hometown = null,
    string? City = null,
    string? Work = null,
    // Matching preferences — pages 17 to 22.
    string? InterestedIn = null,
    int? MinAge = null,
    int? MaxAge = null,
    bool? AgeIsFlexible = null,
    string? Track = null,
    string? Outcome = null)
{
    public static RegistrationAnswers Empty { get; } = new();

    /// <summary>The personal details dropped, leaving whatever has not been promoted yet.</summary>
    public RegistrationAnswers WithoutProfile() =>
        this with
        {
            Name = null,
            Nickname = null,
            Gender = null,
            GenderIsPublic = null,
            DateOfBirth = null,
            HeightCm = null,
            Hometown = null,
            City = null,
            Work = null,
        };

    /// <summary>The matching preferences dropped, leaving whatever has not been promoted yet.</summary>
    public RegistrationAnswers WithoutPreferences() =>
        this with
        {
            InterestedIn = null,
            MinAge = null,
            MaxAge = null,
            AgeIsFlexible = null,
            Track = null,
            Outcome = null,
        };

    /// <summary>
    /// True when nothing is left to keep. The draft row is dropped at that point rather than left
    /// behind as an empty document that later reads would have to interpret.
    /// </summary>
    public bool IsEmpty => this == Empty;
}
