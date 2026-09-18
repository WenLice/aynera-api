using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Common;
using Aynera.Domain.Preferences.Enums;
using Aynera.Domain.Preferences.Requests;
using Aynera.Domain.Preferences.Responses;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// <c>preferences/me</c> — the member's §6 hard filters. Private to them; never on a profile.
/// </summary>
[Collection("Integration")]
public sealed class MemberPreferencesEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string NewPhone() => "98" + Random.Shared.NextInt64(10_000_000, 99_999_999);

    private static async Task<ApiResponse<T>?> Body<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var phone = NewPhone();

        await client.PostAsJsonAsync("/members/register/phone", new StartPhoneRegistrationRequest(phone));
        var code = factory.Sms.GetCode("+91" + phone)!;
        var verify = await client.PostAsJsonAsync(
            "/members/register/phone/verify",
            new VerifyPhoneRegistrationRequest(phone, code));

        var tokens = (await Body<TokenResponse>(verify))!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    [Fact]
    public async Task Unset_ReadsAsNull_ThenSavesAndReadsBack()
    {
        var client = await SignedInClientAsync();

        var before = await Body<MemberPreferencesDto?>(await client.GetAsync("/preferences/me"));
        Assert.True(before!.Success);
        Assert.Null(before.Data);

        var saved = await client.PutAsJsonAsync("/preferences/me", new UpdateMemberPreferencesRequest(
            InterestedIn.Everyone,
            26,
            34,
            IntentOutcome.Legacy,
            AgeIsFlexible: true));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var dto = (await Body<MemberPreferencesDto>(saved))!.Data!;
        Assert.Equal("Everyone", dto.InterestedIn);
        Assert.Equal(26, dto.MinAge);
        Assert.Equal(34, dto.MaxAge);
        Assert.True(dto.AgeIsFlexible);
        Assert.Equal("Legacy", dto.IntentOutcome);
        // The track is derived from the outcome, never sent.
        Assert.Equal("Intent", dto.RelationshipTrack);

        var after = (await Body<MemberPreferencesDto?>(await client.GetAsync("/preferences/me")))!.Data!;
        Assert.Equal("Legacy", after.IntentOutcome);
    }

    [Fact]
    public async Task SecondSave_Replaces()
    {
        var client = await SignedInClientAsync();

        await client.PutAsJsonAsync("/preferences/me", new UpdateMemberPreferencesRequest(
            InterestedIn.Male, 24, 32, IntentOutcome.Platonic, AgeIsFlexible: true));

        var second = await client.PutAsJsonAsync("/preferences/me", new UpdateMemberPreferencesRequest(
            InterestedIn.Female, 30, 40, IntentOutcome.Prospect));

        var dto = (await Body<MemberPreferencesDto>(second))!.Data!;
        Assert.Equal("Female", dto.InterestedIn);
        Assert.Equal(30, dto.MinAge);
        Assert.False(dto.AgeIsFlexible);
        Assert.Equal("Fluid", PreferencesTrackOf("Platonic"));
        Assert.Equal("Intent", dto.RelationshipTrack);
    }

    [Theory]
    [InlineData(17, 30)]   // below the floor
    [InlineData(24, 46)]   // above the ceiling
    [InlineData(34, 30)]   // inverted
    public async Task OutOfRangeAges_AreRefused(int minAge, int maxAge)
    {
        var client = await SignedInClientAsync();

        var response = await client.PutAsJsonAsync("/preferences/me", new UpdateMemberPreferencesRequest(
            InterestedIn.Everyone, minAge, maxAge, IntentOutcome.Prospect));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await Body<object?>(response))!.ErrorCode);
    }

    [Fact]
    public async Task Anonymous_IsRefused()
    {
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/preferences/me", new UpdateMemberPreferencesRequest(
            InterestedIn.Everyone, 24, 32, IntentOutcome.Prospect));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string PreferencesTrackOf(string outcome) =>
        Domain.Preferences.Validators.PreferenceRules
            .TrackFor(Enum.Parse<IntentOutcome>(outcome))
            .ToString();
}
