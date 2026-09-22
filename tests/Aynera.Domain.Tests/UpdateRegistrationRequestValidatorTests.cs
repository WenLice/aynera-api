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
}
