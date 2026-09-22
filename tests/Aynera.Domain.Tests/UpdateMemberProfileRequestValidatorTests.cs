using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Auth.Validators;

namespace Aynera.Domain.Tests;

public class UpdateMemberProfileRequestValidatorTests
{
    private static readonly UpdateMemberProfileRequestValidator Validator = new();

    private static DateOnly Adult => DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25));

    private static UpdateMemberProfileRequest Valid(
        string name = "Ada Lovelace",
        string? nickname = null,
        DateOnly? dateOfBirth = null,
        int? heightCm = null,
        string city = "Delhi",
        string? hometown = "Pune") =>
        new(name, Gender.Female, dateOfBirth ?? Adult, city, nickname, heightCm, hometown);

    [Fact]
    public void MinimalRequest_IsValid() =>
        Assert.True(Validator.Validate(Valid()).IsValid);

    [Fact]
    public void EveryOptionalField_IsValid()
    {
        var request = new UpdateMemberProfileRequest(
            "Ada",
            Gender.ThirdGender,
            Adult,
            "Bangalore",
            Nickname: "Adz",
            HeightCm: 170,
            Hometown: "Pune",
            Work: "Writes compilers",
            Religion: "Hindu");

        Assert.True(Validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsRequired(string name) =>
        Assert.Contains(
            Validator.Validate(Valid(name: name)).Errors,
            e => e.PropertyName == nameof(UpdateMemberProfileRequest.Name));

    [Fact]
    public void Name_TooLong_IsRejected() =>
        Assert.False(Validator.Validate(Valid(name: new string('a', 151))).IsValid);

    [Fact]
    public void Nickname_Omitted_IsValid() =>
        Assert.True(Validator.Validate(Valid(nickname: null)).IsValid);

    /// <summary>
    /// Hometown has its own page in the app's registration, so a profile without one is not a
    /// shape the product allows. Blank counts as missing — it is free text, not a flag.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hometown_IsRequired(string? hometown) =>
        Assert.Contains(
            Validator.Validate(Valid(hometown: hometown)).Errors,
            e => e.PropertyName == nameof(UpdateMemberProfileRequest.Hometown));

    [Fact]
    public void Nickname_TooShort_IsRejected() =>
        Assert.Contains(
            Validator.Validate(Valid(nickname: "A")).Errors,
            e => e.PropertyName == nameof(UpdateMemberProfileRequest.Nickname));

    [Fact]
    public void Underage_IsRejected()
    {
        var justUnder = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-AgeRules.MinimumAgeYears).AddDays(1);

        Assert.Contains(
            Validator.Validate(Valid(dateOfBirth: justUnder)).Errors,
            e => e.PropertyName == nameof(UpdateMemberProfileRequest.DateOfBirth));
    }

    [Fact]
    public void ExactlyMinimumAge_IsValid()
    {
        var exactly = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-AgeRules.MinimumAgeYears);

        Assert.True(Validator.Validate(Valid(dateOfBirth: exactly)).IsValid);
    }

    [Theory]
    [InlineData(MemberProfileRules.HeightMinCm - 1, false)]
    [InlineData(MemberProfileRules.HeightMinCm, true)]
    [InlineData(175, true)]
    [InlineData(MemberProfileRules.HeightMaxCm, true)]
    [InlineData(MemberProfileRules.HeightMaxCm + 1, false)]
    public void Height_IsBounded(int heightCm, bool expected) =>
        Assert.Equal(expected, Validator.Validate(Valid(heightCm: heightCm)).IsValid);

    [Fact]
    public void City_IsRequired() =>
        Assert.Contains(
            Validator.Validate(Valid(city: "")).Errors,
            e => e.PropertyName == nameof(UpdateMemberProfileRequest.City));

    /// <summary>The app's height picker must never produce a value the API rejects.</summary>
    [Theory]
    [InlineData(137)]
    [InlineData(213)]
    public void AppHeightPickerBounds_AreAccepted(int heightCm) =>
        Assert.True(Validator.Validate(Valid(heightCm: heightCm)).IsValid);
}
