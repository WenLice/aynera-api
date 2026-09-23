using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Statics;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Aynera.Api.IntegrationTests;

/// <summary>The face check over HTTP, with the test provider standing in for AWS.</summary>
[Collection("Integration")]
public sealed class LivenessEndpointsTests(AuthApiFactory factory)
{
    /// <summary>Photos are matched against the verified face, so there is nothing to match before it.</summary>
    [Fact]
    public async Task Photos_BeforeTheFaceCheck_AreRefused()
    {
        using var client = Client(await CreateMemberTokenAsync());

        using var response = await PostPhotoAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("photo_face_check_required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Photos_AfterAPass_AreAccepted_AndMatched()
    {
        using var client = Client(await CreateMemberTokenAsync());
        await FaceCheck.PassAsync(client);

        using var response = await PostPhotoAsync(client);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"faceMatchStatus\":\"Matched\"", body);
    }

    [Fact]
    public async Task Pass_IsRecorded_AndReadBack()
    {
        using var client = Client(await CreateMemberTokenAsync());

        var sessionId = await StartAsync(client);
        using (var complete = await client.PostAsync($"/liveness/{sessionId}/Complete", null))
        {
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        }

        using var mine = await client.GetAsync("/liveness/me");
        var body = await mine.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Contains("\"passed\":true", body);
    }

    [Fact]
    public async Task AnotherMembersSession_CannotBeCompleted()
    {
        using var owner = Client(await CreateMemberTokenAsync());
        var sessionId = await StartAsync(owner);

        using var stranger = Client(await CreateMemberTokenAsync());
        using var response = await stranger.PostAsync($"/liveness/{sessionId}/Complete", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoCheckYet_IsNotFound()
    {
        using var client = Client(await CreateMemberTokenAsync());
        using var response = await client.GetAsync("/liveness/me");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string> StartAsync(HttpClient client)
    {
        using var start = await client.PostAsync("/liveness/Start", null);
        var body = await start.Content.ReadAsStringAsync();
        Assert.True(start.StatusCode == HttpStatusCode.OK, $"Start failed: {start.StatusCode} {body}");
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Contains("session=", data.GetProperty("pageUrl").GetString());
        return data.GetProperty("sessionId").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostPhotoAsync(HttpClient client)
    {
        using var image = new Image<Rgb24>(32, 32, new Rgb24(180, 140, 120));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(stream.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "photos", "face.jpg");
        return await client.PostAsync("/photos/Upload", form);
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
            Id = Guid.NewGuid(), UserName = phone, PhoneNumber = phone, PhoneNumberConfirmed = true,
            Email = $"liveness-{Guid.NewGuid():N}@example.test", EmailConfirmed = true, AccountKind = AccountKind.Member
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AuthRoles.Member)).Succeeded);
        var account = (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, CancellationToken.None))!;
        return sp.GetRequiredService<ITokenService>()
            .CreateAccessToken(account, AuthAudiences.Member, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow).AccessToken;
    }
}
