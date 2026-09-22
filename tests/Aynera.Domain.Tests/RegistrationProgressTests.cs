using Aynera.Domain.Answers.Records;
using Aynera.Domain.Registration.Records;
using Aynera.Domain.Registration.Statics;

namespace Aynera.Domain.Tests;

public class RegistrationProgressTests
{
    private static DateOnly Adult => DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25));

    [Fact]
    public void NothingAnswered_NextStepIsPhone()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: false, emailConfirmed: false, RegistrationAnswers.Empty);

        Assert.Empty(completed);
        Assert.Equal(RegistrationProgress.Phone, RegistrationProgress.NextStep(completed));
    }

    [Fact]
    public void PhoneVerifiedOnly_NextStepIsEmail()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true, emailConfirmed: false, RegistrationAnswers.Empty);

        Assert.Equal([RegistrationProgress.Phone], completed);
        Assert.Equal(RegistrationProgress.Email, RegistrationProgress.NextStep(completed));
    }

    [Fact]
    public void VerifiedWithNoAnswers_ResumesAtTheNameStep()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true, emailConfirmed: true, RegistrationAnswers.Empty);

        Assert.Equal(RegistrationProgress.You, RegistrationProgress.NextStep(completed));
    }

    /// <summary>
    /// The point of the whole design: a member who quit after the name step comes back to the
    /// step after it, not to the beginning.
    /// </summary>
    [Fact]
    public void NameAnswered_ResumesAtTheSelfStep()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            new RegistrationAnswers(Name: "Ada Lovelace"));

        Assert.Contains(RegistrationProgress.You, completed);
        Assert.Equal(RegistrationProgress.Self, RegistrationProgress.NextStep(completed));
    }

    /// <summary>The app collects birth date and hometown on one page, so one alone does not finish it.</summary>
    [Fact]
    public void BirthDateWithoutHometown_DoesNotCompleteTheBirthStep()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            new RegistrationAnswers(Name: "Ada", Gender: "Female", DateOfBirth: Adult));

        Assert.DoesNotContain(RegistrationProgress.Birth, completed);
        Assert.Equal(RegistrationProgress.Birth, RegistrationProgress.NextStep(completed));
    }

    /// <summary>
    /// The personal details being finished does not finish registration — the flow carries on into
    /// the matching preferences, so the member resumes there rather than at the end.
    /// </summary>
    [Fact]
    public void ProfileAnswered_ResumesAtTheLookingStep()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            Complete());

        Assert.Contains(RegistrationProgress.Life, completed);
        Assert.Equal(RegistrationProgress.Looking, RegistrationProgress.NextStep(completed));
    }

    /// <summary>A track without its outcome is half an answer, so the intent page is not done.</summary>
    [Fact]
    public void TrackWithoutOutcome_DoesNotCompleteTheIntentStep()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            Complete() with { InterestedIn = "Male", MinAge = 24, Track = "Intent" });

        Assert.Contains(RegistrationProgress.Looking, completed);
        Assert.DoesNotContain(RegistrationProgress.Intent, completed);
        Assert.Equal(RegistrationProgress.Intent, RegistrationProgress.NextStep(completed));
    }

    /// <summary>
    /// The preference pages being done does not end the flow either — the optional everyday,
    /// belief and vibe questions come after them.
    /// </summary>
    [Fact]
    public void PreferencesAnswered_ResumesAtTheLifestyleStep()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            CompleteWithPreferences());

        Assert.Contains(RegistrationProgress.Intent, completed);
        Assert.Equal(RegistrationProgress.Lifestyle, RegistrationProgress.NextStep(completed));
    }

    [Fact]
    public void EverythingAnswered_HasNoNextStep()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            CompleteWithPreferences(),
            AllCategoriesAnswered());

        Assert.Equal(RegistrationProgress.Ordered, completed);
        Assert.Null(RegistrationProgress.NextStep(completed));
    }

    /// <summary>
    /// Every question in these categories is optional, so a category counts as done once it holds
    /// anything — there is no way to tell "answered nothing" from "not reached yet".
    /// </summary>
    [Fact]
    public void OneAnswerFinishesItsCategory()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            CompleteWithPreferences(),
            MemberProfileAnswersRecord.Empty(Guid.NewGuid()) with
            {
                Lifestyle = new Dictionary<string, MemberAnswer> { ["drink"] = new("no") },
            });

        Assert.Contains(RegistrationProgress.Lifestyle, completed);
        Assert.DoesNotContain(RegistrationProgress.Beliefs, completed);
        Assert.Equal(RegistrationProgress.Beliefs, RegistrationProgress.NextStep(completed));
    }

    /// <summary>An answers row that exists but is empty leaves every category outstanding.</summary>
    [Fact]
    public void AnEmptyAnswersRow_FinishesNothing()
    {
        var completed = RegistrationProgress.Completed(
            phoneConfirmed: true,
            emailConfirmed: true,
            CompleteWithPreferences(),
            MemberProfileAnswersRecord.Empty(Guid.NewGuid()));

        Assert.Equal(RegistrationProgress.Lifestyle, RegistrationProgress.NextStep(completed));
    }

    private static MemberProfileAnswersRecord AllCategoriesAnswered() =>
        new(
            Guid.NewGuid(),
            new Dictionary<string, MemberAnswer> { ["drink"] = new("no") },
            new Dictionary<string, MemberAnswer> { ["faith"] = new("not-religious") },
            ["reading"]);

    /// <summary>
    /// An open upper end is an answer, not a gap, so preferences are complete without a maximum age.
    /// </summary>
    [Fact]
    public void OpenUpperAgeEnd_StillCompletesPreferences() =>
        Assert.True(RegistrationProgress.IsPreferencesComplete(
            CompleteWithPreferences() with { MaxAge = null }));

    [Theory]
    [InlineData(null, 24, "Intent", "Prospect")]
    [InlineData("Male", null, "Intent", "Prospect")]
    [InlineData("Male", 24, null, "Prospect")]
    [InlineData("Male", 24, "Intent", null)]
    public void AnyRequiredPreferenceMissing_IsNotComplete(
        string? interestedIn,
        int? minAge,
        string? track,
        string? outcome) =>
        Assert.False(RegistrationProgress.IsPreferencesComplete(
            CompleteWithPreferences() with
            {
                InterestedIn = interestedIn,
                MinAge = minAge,
                Track = track,
                Outcome = outcome,
            }));

    /// <summary>
    /// Answers can arrive in any order — the flow's order is a UI choice, not a storage rule — so
    /// completeness must not depend on the sequence they were written in.
    /// </summary>
    [Fact]
    public void AnswersOutOfOrder_StillCompleteTheProfile() =>
        Assert.True(RegistrationProgress.IsProfileComplete(
            new RegistrationAnswers(
                City: "Bangalore",
                Hometown: "Pune",
                DateOfBirth: Adult,
                Gender: "Female",
                Name: "Ada")));

    [Theory]
    [InlineData(null, "Female", "Bangalore", "Pune")]
    [InlineData("Ada", null, "Bangalore", "Pune")]
    [InlineData("Ada", "Female", null, "Pune")]
    [InlineData("Ada", "Female", "Bangalore", null)]
    public void AnyRequiredAnswerMissing_IsNotComplete(
        string? name,
        string? gender,
        string? city,
        string? hometown) =>
        Assert.False(RegistrationProgress.IsProfileComplete(
            new RegistrationAnswers(
                Name: name,
                Gender: gender,
                City: city,
                Hometown: hometown,
                DateOfBirth: Adult)));

    [Fact]
    public void MissingDateOfBirth_IsNotComplete() =>
        Assert.False(RegistrationProgress.IsProfileComplete(Complete() with { DateOfBirth = null }));

    /// <summary>Whitespace is not an answer — it would otherwise pass into a NOT NULL column.</summary>
    [Fact]
    public void WhitespaceName_IsNotComplete() =>
        Assert.False(RegistrationProgress.IsProfileComplete(Complete() with { Name = "   " }));

    private static RegistrationAnswers Complete() =>
        new(
            Name: "Ada Lovelace",
            Gender: "Female",
            DateOfBirth: Adult,
            City: "Bangalore",
            Hometown: "Pune");

    private static RegistrationAnswers CompleteWithPreferences() =>
        Complete() with
        {
            InterestedIn = "Male",
            MinAge = 24,
            MaxAge = 32,
            Track = "Intent",
            Outcome = "Prospect",
        };
}
