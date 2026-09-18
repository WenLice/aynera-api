using Aynera.Domain.Venues.Enums;
using Aynera.Domain.Venues.Requests;
using Aynera.Domain.Venues.Validators;

namespace Aynera.Domain.Tests;

public class VenueValidationTests
{
    private static readonly Guid AnyCity = Guid.NewGuid();

    [Theory]
    [InlineData("Cafe", true)]
    [InlineData("cafe", true)]
    [InlineData("EventPlace", true)]
    [InlineData("eventplace", true)]
    [InlineData("Restaurant", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BeKnownVenueType_MatchesExpected(string? value, bool expected) =>
        Assert.Equal(expected, VenueValidation.BeKnownVenueType(value));

    [Fact]
    public void TryParseType_ParsesCaseInsensitively()
    {
        Assert.True(VenueValidation.TryParseType("eventPLACE", out var type));
        Assert.Equal(VenueType.EventPlace, type);
    }

    [Fact]
    public void Create_ValidRequest_Passes()
    {
        var result = new CreateVenueRequestValidator().Validate(
            new CreateVenueRequest(
                Name: "Blue Tokai",
                Type: "Cafe",
                CityId: AnyCity,
                Area: "Hauz Khas",
                Address: "12 Aurobindo Marg, New Delhi",
                ContactName: "Priya",
                ContactEmail: "priya@bluetokai.example",
                ContactPhoneE164: "+919876543210"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "Cafe", "priya@x.com")]       // missing name
    [InlineData("Blue Tokai", "Bar", "priya@x.com")] // unknown type
    [InlineData("Blue Tokai", "Cafe", "not-an-email")] // bad email
    public void Create_InvalidRequest_Fails(string name, string type, string email)
    {
        var result = new CreateVenueRequestValidator().Validate(
            new CreateVenueRequest(
                Name: name,
                Type: type,
                CityId: AnyCity,
                Area: "Hauz Khas",
                Address: "12 Aurobindo Marg",
                ContactName: "Priya",
                ContactEmail: email,
                ContactPhoneE164: "+919876543210"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Create_EmptyCity_Fails()
    {
        var result = new CreateVenueRequestValidator().Validate(
            new CreateVenueRequest(
                Name: "Blue Tokai",
                Type: "Cafe",
                CityId: Guid.Empty,
                Area: "Hauz Khas",
                Address: "12 Aurobindo Marg",
                ContactName: "Priya",
                ContactEmail: "priya@x.com",
                ContactPhoneE164: "+919876543210"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Update_PartialRequest_OnlyValidatesProvidedFields()
    {
        var result = new UpdateVenueRequestValidator().Validate(
            new UpdateVenueRequest(IsActive: false));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_UnknownType_Fails()
    {
        var result = new UpdateVenueRequestValidator().Validate(
            new UpdateVenueRequest(Type: "Nightclub"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void HeadsUp_ValidFutureRequest_Passes()
    {
        var result = new SendVenueHeadsUpRequestValidator().Validate(
            new SendVenueHeadsUpRequest(
                VisitOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
                PartySize: 4,
                Note: "Window seats"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void HeadsUp_PastDate_Fails()
    {
        var result = new SendVenueHeadsUpRequestValidator().Validate(
            new SendVenueHeadsUpRequest(
                VisitOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                PartySize: 4));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(10_001)]
    public void HeadsUp_InvalidPartySize_Fails(int partySize)
    {
        var result = new SendVenueHeadsUpRequestValidator().Validate(
            new SendVenueHeadsUpRequest(
                VisitOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                PartySize: partySize));

        Assert.False(result.IsValid);
    }
}
