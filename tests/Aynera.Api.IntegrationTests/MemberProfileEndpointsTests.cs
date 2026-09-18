using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Common;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// <c>PUT /members/me/profile</c> — the basic details the app collects across its "you", "basics" and
/// "life" steps and sends in one go, on an account that so far only has a verified phone.
/// </summary>
[Collection("Integration")]
public sealed class MemberProfileEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static DateOnly Adult => DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-27));

    private static string NewPhone() => "98" + Random.Shared.NextInt64(10_000_000, 99_999_999);

    private static async Task<ApiResponse<T>?> Body<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json);

    /// <summary>Signs in the way the app does: phone, SMS code, tokens — no profile row yet.</summary>
    private async Task<HttpClient> PhoneVerifiedClientAsync()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        var start = await client.PostAsJsonAsync("/members/register/phone", new StartPhoneRegistrationRequest(phone));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var code = factory.Sms.GetCode("+91" + phone)!;
        var verify = await client.PostAsJsonAsync(
            "/members/register/phone/verify",
            new VerifyPhoneRegistrationRequest(phone, code));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var tokens = (await Body<TokenResponse>(verify))!.Data!;
        Assert.Null(tokens.Account.Profile);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task FirstSave_CreatesTheProfile_AndResolvesTheCity()
    {
        var client = await PhoneVerifiedClientAsync();

        var saved = await client.PutAsJsonAsync("/members/me/profile", new UpdateMemberProfileRequest(
            "  Ada Lovelace  ",
            Gender.Female,
            Adult,
            "  bangalore ",
            Nickname: " Adz ",
            HeightCm: 168,
            Hometown: " Pune ",
            Work: " Writes compilers "));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var profile = (await Body<AuthAccountDto>(saved))!.Data!.Profile;
        Assert.NotNull(profile);
        Assert.Equal("Ada Lovelace", profile!.Name);
        Assert.Equal("Adz", profile.Nickname);
        Assert.Equal("Female", profile.Gender);
        Assert.Equal(168, profile.HeightCm);
        Assert.Equal("Pune", profile.Hometown);
        Assert.Equal("Writes compilers", profile.Work);
        // The typed city is resolved to the catalog's own spelling and key.
        Assert.Equal("Bangalore", profile.City);
        Assert.NotEqual(Guid.Empty, profile.CityId);

        // And it is there on the next read.
        var me = (await Body<AuthAccountDto>(await client.GetAsync("/members/me")))!.Data!;
        Assert.Equal("Ada Lovelace", me.Profile!.Name);
        Assert.Equal(profile.CityId, me.Profile.CityId);
    }

    [Fact]
    public async Task SecondSave_ReplacesEveryField_ClearingOmittedOptionalOnes()
    {
        var client = await PhoneVerifiedClientAsync();

        await client.PutAsJsonAsync("/members/me/profile", new UpdateMemberProfileRequest(
            "Ada Lovelace",
            Gender.Female,
            Adult,
            "Delhi",
            Nickname: "Adz",
            HeightCm: 168,
            Hometown: "Pune",
            Work: "Writes compilers",
            Religion: "Hindu"));

        // A full replace: everything left out is cleared, not kept.
        var second = await client.PutAsJsonAsync("/members/me/profile", new UpdateMemberProfileRequest(
            "Ada",
            Gender.Other,
            Adult,
            "Bangalore"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var profile = (await Body<AuthAccountDto>(second))!.Data!.Profile!;
        Assert.Equal("Ada", profile.Name);
        Assert.Equal("Other", profile.Gender);
        Assert.Equal("Bangalore", profile.City);
        Assert.Null(profile.Nickname);
        Assert.Null(profile.HeightCm);
        Assert.Null(profile.Hometown);
        Assert.Null(profile.Work);
        Assert.Null(profile.Religion);
    }

    /// <summary>
    /// The app sends <c>gender</c> as the name, which is also how every response reports it.
    /// Typed clients serialise the enum as a number, so only raw JSON covers this.
    /// </summary>
    [Theory]
    [InlineData("\"Female\"", "Female")]
    [InlineData("\"Other\"", "Other")]
    [InlineData("\"PreferNotToSay\"", "PreferNotToSay")]
    [InlineData("1", "Female")]
    [InlineData("3", "PreferNotToSay")]
    public async Task Gender_IsAcceptedByNameAndByNumber(string genderJson, string expected)
    {
        var client = await PhoneVerifiedClientAsync();

        var json = $$"""
            {
              "name": "Ada Lovelace",
              "gender": {{genderJson}},
              "dateOfBirth": "{{Adult:yyyy-MM-dd}}",
              "city": "Delhi"
            }
            """;

        var response = await client.PutAsync(
            "/members/me/profile",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, (await Body<AuthAccountDto>(response))!.Data!.Profile!.Gender);
    }

    [Fact]
    public async Task UnknownCity_IsRefused_AndLeavesNoProfile()
    {
        var client = await PhoneVerifiedClientAsync();

        var response = await client.PutAsJsonAsync("/members/me/profile", new UpdateMemberProfileRequest(
            "Ada Lovelace",
            Gender.Female,
            Adult,
            "Atlantis"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("city_not_supported", (await Body<object?>(response))!.ErrorCode);

        var me = (await Body<AuthAccountDto>(await client.GetAsync("/members/me")))!.Data!;
        Assert.Null(me.Profile);
    }

    [Fact]
    public async Task Underage_IsRefused()
    {
        var client = await PhoneVerifiedClientAsync();

        var response = await client.PutAsJsonAsync("/members/me/profile", new UpdateMemberProfileRequest(
            "Kid User",
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)),
            "Delhi"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsRefused()
    {
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/members/me/profile", new UpdateMemberProfileRequest(
            "Ada Lovelace",
            Gender.Female,
            Adult,
            "Delhi"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
