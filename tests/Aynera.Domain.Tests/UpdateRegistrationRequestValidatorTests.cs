using Aynera.Domain.Answers.Records;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Registration.Requests;
using Aynera.Domain.Registration.Validators;

namespace Aynera.Domain.Tests;

/// <summary>
/// The rule this validator exists to enforce: judge the request on what it carries, never on what
/// it omits. A page sends its own fields and nothing else.
/// </summary>
public class UpdateRegistrationRequestValidatorTests
{
    private static readonly UpdateRegistrationRequestValidator Validator = new();

    private static DateOnly Adult => DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25));

    [Fact]
    public void NameAlone_IsValid() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Name: "Ada Lovelace")).IsValid);

    [Fact]
    public void GenderAlone_IsValid() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Gender: Gender.Female)).IsValid);

    [Fact]
    public void CityAlone_IsValid() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(City: "Bangalore")).IsValid);

    /// <summary>A request that changes nothing is refused rather than silently accepted.</summary>
    [Fact]
    public void EmptyRequest_IsRefused()
    {
        var result = Validator.Validate(new UpdateRegistrationRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one answer"));
    }

    /// <summary>
    /// The age gate runs on the page that collects the date, so an underage member is stopped
    /// there rather than at the end of registration.
    /// </summary>
    [Fact]
    public void UnderageDateOfBirth_IsRefusedOnTheBirthPage()
    {
        var child = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-15));
        var result = Validator.Validate(new UpdateRegistrationRequest(DateOfBirth: child));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AdultDateOfBirth_IsValid() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(DateOfBirth: Adult)).IsValid);

    [Fact]
    public void OverLongName_IsRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Name: new string('a', 151))).IsValid);

    /// <summary>A required field that is sent must carry a value — omission is the way to skip it.</summary>
    [Fact]
    public void BlankName_IsRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Name: "  ")).IsValid);

    [Fact]
    public void BlankHometown_IsRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Hometown: "")).IsValid);

    /// <summary>Optional values are clearable, which is how a nickname reverts to an initial.</summary>
    [Fact]
    public void BlankNickname_IsAllowedAndClears() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Nickname: "")).IsValid);

    [Fact]
    public void BlankWork_IsAllowedAndClears() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Work: "")).IsValid);

    [Fact]
    public void ShortNickname_IsRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Nickname: "A")).IsValid);

    [Theory]
    [InlineData(50)]
    [InlineData(300)]
    public void HeightOutOfRange_IsRefused(int heightCm) =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(HeightCm: heightCm)).IsValid);

    [Fact]
    public void HeightInRange_IsValid() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(HeightCm: 168)).IsValid);

    [Fact]
    public void UnknownGender_IsRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Gender: (Gender)99)).IsValid);

    [Fact]
    public void AWholePageOfAnswers_IsValid() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(
            Name: "Ada Lovelace",
            Nickname: "Ada",
            Gender: Gender.Female,
            GenderIsPublic: true,
            DateOfBirth: Adult,
            HeightCm: 168,
            Hometown: "Pune",
            City: "Bangalore",
            Work: "Writes compilers")).IsValid);

    // ---- prompts, extras, notifications ----------------------------------------------------

    [Fact]
    public void Prompts_TypedOrForRecording_AreValid() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Prompts:
            [new MemberPromptAnswer("know", "A sentence."), new MemberPromptAnswer("soft")])).IsValid);

    [Fact]
    public void Prompts_MoreThanThree_AreRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Prompts:
            [new("a"), new("b"), new("c"), new("d")])).IsValid);

    [Fact]
    public void Prompts_SameOneTwice_IsRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Prompts: [new("know"), new("know")])).IsValid);

    /// <summary>The id becomes part of an object key, so anything path-like is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("../x")]
    [InlineData("Know")]
    [InlineData("has space")]
    [InlineData("a-very-long-prompt-id-that-is-too-long")]
    public void PromptId_MustBePathSafe(string promptId) =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Prompts: [new(promptId)])).IsValid);

    [Fact]
    public void PromptText_OverTheLimit_IsRefused() =>
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Prompts:
            [new MemberPromptAnswer("know", new string('a', 301))])).IsValid);

    [Fact]
    public void EmptyPromptList_IsAValidChange() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Prompts: [])).IsValid);

    [Fact]
    public void NotificationsAlone_IsAValidPage() =>
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(NotificationsOn: false)).IsValid);

    [Fact]
    public void Dealbreaker_Empty_ClearsAndIsValid_TooLong_IsRefused()
    {
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Dealbreaker: "")).IsValid);
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Dealbreaker: new string('a', 501))).IsValid);
    }

    [Fact]
    public void Rhythm_NeedsAnOptionForEveryQuestion()
    {
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Rhythm:
            new Dictionary<string, string> { ["socialEnergy"] = "Small groups" })).IsValid);
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Rhythm:
            new Dictionary<string, string> { ["socialEnergy"] = " " })).IsValid);
    }

    /// <summary>Only the three genders exist; hiding one is genderIsPublic, not a fourth value.</summary>
    [Fact]
    public void OnlyTheThreeGenders_AreAccepted()
    {
        Assert.False(Validator.Validate(new UpdateRegistrationRequest(Gender: (Gender)3)).IsValid);
        Assert.True(Validator.Validate(new UpdateRegistrationRequest(Gender: Gender.ThirdGender, GenderIsPublic: false)).IsValid);
    }
}
