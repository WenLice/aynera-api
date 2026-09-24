using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Registration.Responses;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// The end of registration, through the real endpoints: after the question pages the member is
/// sent to the face check, their photos, the prompts (typed or recorded), notifications, then
/// consent — and only accepting every document finishes the walk.
/// </summary>
[Collection("Integration")]
public sealed class ConsentProgressEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FaceCheck_Photos_Prompts_Notifications_ThenConsent_ThenNothingLeft()
    {
        using var client = Client(await CreateMemberTokenAsync());

        await PatchAsync(client, new
        {
            name = "Riya", gender = "Female", genderIsPublic = true, dateOfBirth = "1996-04-12",
            hometown = "Pune", city = "Bangalore", work = "Architect",
            interestedIn = "Male", minAge = 24, maxAge = 32, ageIsFlexible = false,
            track = "Intent", outcome = "Prospect",
            lifestyle = new { drink = new { option = "Sometimes", @public = true } },
            beliefs = new { faith = new { option = "Spiritual, not ritual", @public = true } },
            vibe = new[] { "Reading", "Photography", "Cooking", "Music", "Board games" },
        });
        // The face check comes first: the photos are matched against the face it verifies.
        Assert.Equal("liveness", (await ProgressAsync(client)).NextStep);
        await FaceCheck.PassAsync(client);
        Assert.Equal("photos", (await ProgressAsync(client)).NextStep);

        using (var form = new MultipartFormDataContent())
        {
            for (var i = 0; i < 5; i++)
            {
                var file = new ByteArrayContent(Jpeg());
                file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                form.Add(file, "photos", $"photo{i}.jpg");
            }

            using var upload = await client.PostAsync("/photos/Upload", form);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        // The intro video is optional and not a step: photos lead straight to the prompts.
        Assert.Equal("voice", (await ProgressAsync(client)).NextStep);

        // One typed, one to be recorded — the recording is what finishes the step.
        await PatchAsync(client, new { prompts = new object[] { new { promptId = "know" }, new { promptId = "soft", text = "Kindness." } } });
        Assert.Equal("voice", (await ProgressAsync(client)).NextStep);
        await UploadVoiceAsync(client, "know", HttpStatusCode.OK);

        var afterVoice = await ProgressAsync(client);
        Assert.Equal("notifications", afterVoice.NextStep);
        Assert.True(afterVoice.ProfileAnswers!.Prompts.Single(p => p.PromptId == "know").HasAudio);

        // "Not now" is an answer.
        await PatchAsync(client, new { notificationsOn = false });
        var afterNotifications = await ProgressAsync(client);
        Assert.Equal("consent", afterNotifications.NextStep);
        Assert.False(afterNotifications.Settings!.NotifyIntroductions);
        Assert.False(afterNotifications.Settings.NotifyReplies);
        Assert.False(afterNotifications.Settings.NotifyWeekendSurprise);

        // The app sends one call per document, in sequence.
        foreach (var kind in new[] { "Terms", "Privacy" })
        {
            await AcceptAsync(client, kind, "1.0");
        }

        Assert.Equal("consent", (await ProgressAsync(client)).NextStep);

        await AcceptAsync(client, "CommunityGuidelines", "1.0");
        var done = await ProgressAsync(client);
        Assert.Null(done.NextStep);
        Assert.Contains("consent", done.Completed);

        // Accepting again is harmless — the app may resend the set after a partial failure.
        await AcceptAsync(client, "Terms", "1.0");
        Assert.Null((await ProgressAsync(client)).NextStep);
    }

    /// <summary>
    /// A recording must answer a prompt the member holds, is stored under the prompt's own name and
    /// comes back with a playback link; dropping the prompt removes it.
    /// </summary>
    [Fact]
    public async Task VoiceAnswers_OnlyForChosenPrompts_AndFollowThePromptList()
    {
        using var client = Client(await CreateMemberTokenAsync());

        await UploadVoiceAsync(client, "know", HttpStatusCode.BadRequest, "voice_prompt_not_chosen");

        await PatchAsync(client, new { prompts = new object[] { new { promptId = "know" }, new { promptId = "soft" } } });
        await UploadVoiceAsync(client, "know", HttpStatusCode.OK);
        await UploadVoiceAsync(client, "soft", HttpStatusCode.OK);
        await UploadVoiceAsync(client, "know", HttpStatusCode.OK); // a re-recording replaces, not duplicates

        var listed = await ListVoiceAsync(client);
        Assert.Equal(["know", "soft"], listed.Select(x => x.GetProperty("promptId").GetString()).Order());
        Assert.All(listed, x => Assert.False(string.IsNullOrEmpty(x.GetProperty("url").GetString())));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Aynera.Persistence.AyneraDbContext>();
            var keys = db.MemberMedia
                .Where(m => m.Kind == Aynera.Domain.Media.Enums.MediaKind.VoiceAnswer && m.PromptId == "know")
                .Select(m => m.StorageKey)
                .ToList()
                .Where(k => k.EndsWith("/voice_know.m4a", StringComparison.Ordinal));
            Assert.NotEmpty(keys);
        }

        // Drop "know" from the list: its recording goes, "soft" keeps its own.
        await PatchAsync(client, new { prompts = new object[] { new { promptId = "soft" }, new { promptId = "value", text = "Honesty." } } });
        Assert.Equal(["soft"], (await ListVoiceAsync(client)).Select(x => x.GetProperty("promptId").GetString()));

        using var delete = await client.DeleteAsync("/voice-answers/soft");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Empty(await ListVoiceAsync(client));

        // Not an audio file.
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "audio", "x.jpg");
        form.Add(new StringContent("value"), "promptId");
        using var wrongType = await client.PostAsync("/voice-answers/Upload", form);
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Contains("voice_unsupported_type", await wrongType.Content.ReadAsStringAsync());
    }

    private static async Task UploadVoiceAsync(
        HttpClient client,
        string promptId,
        HttpStatusCode expected,
        string? errorCode = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Enumerable.Range(0, 2048).Select(i => (byte)i).ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mp4");
        form.Add(file, "audio", "answer.m4a");
        form.Add(new StringContent(promptId), "promptId");

        using var response = await client.PostAsync("/voice-answers/Upload", form);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Voice upload {promptId}: {response.StatusCode} {body}");
        if (errorCode is not null) Assert.Contains(errorCode, body);
    }

    private static async Task<List<JsonElement>> ListVoiceAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/voice-answers/GetAll");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"List failed: {response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("data").EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private static async Task PatchAsync(HttpClient client, object page)
    {
        using var response = await client.PatchAsJsonAsync("/members/me/registration", page);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"PATCH failed: {response.StatusCode} {body}");
    }

    private static async Task AcceptAsync(HttpClient client, string kind, string version)
    {
        using var response = await client.PostAsJsonAsync(
            "/admissions/me/consents", new { policyKind = kind, version });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Consent {kind} failed: {response.StatusCode} {body}");
    }

    private static async Task<RegistrationProgressDto> ProgressAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/members/me/registration");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET failed: {response.StatusCode} {body}");
        return JsonSerializer.Deserialize<ApiResponse<RegistrationProgressDto>>(body, Json)!.Data!;
    }

    private static byte[] Jpeg()
    {
        using var image = new Image<Rgb24>(32, 32, new Rgb24(180, 140, 120));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> CreateMemberTokenAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            Email = $"consent-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true,
            AccountKind = AccountKind.Member
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AuthRoles.Member)).Succeeded);

        var account = (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, CancellationToken.None))!;
        return sp.GetRequiredService<ITokenService>()
            .CreateAccessToken(account, AuthAudiences.Member, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow)
            .AccessToken;
    }
}
