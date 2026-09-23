using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common;
using Aynera.Domain.Media.Enums;
using Aynera.Domain.Photos.Responses;
using Aynera.Infrastructure.Storage;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// Uploads still go through the API; these pin where the bytes end up. Every member has a folder
/// named by their id, photos are <c>photo_1</c>…<c>photo_N</c>, the intro video is <c>intro_video</c>.
/// </summary>
[Collection("Integration")]
public sealed class MediaStorageEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Photos_AreStoredInTheMembersFolder_NumberedBySlot()
    {
        var (userId, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);

        var uploaded = await UploadAsync(client, Jpeg(), Jpeg(), Jpeg());

        Assert.Equal([1, 2, 3], uploaded.Select(p => p.SortOrder).ToArray());
        var keys = StoredKeys(userId);
        Assert.Equal(
            [$"{userId:D}/photo_1.jpg", $"{userId:D}/photo_2.jpg", $"{userId:D}/photo_3.jpg"],
            keys);

        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().MemberMedia
            .Where(x => x.UserId == userId && x.Kind == MediaKind.Photo)
            .OrderBy(x => x.Index)
            .ToListAsync();
        Assert.Equal(keys, rows.Select(r => r.StorageKey).ToArray());
    }

    [Fact]
    public async Task DeletedSlot_IsFilledByTheNextUpload_NotSkipped()
    {
        var (userId, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);
        var uploaded = await UploadAsync(client, Jpeg(), Jpeg(), Jpeg());

        var second = uploaded.Single(p => p.SortOrder == 2);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/photos/{second.Id}")).StatusCode);

        var replacement = Assert.Single(await UploadAsync(client, Jpeg()));

        Assert.Equal(2, replacement.SortOrder);
        Assert.Contains($"{userId:D}/photo_2.jpg", StoredKeys(userId));
        Assert.DoesNotContain(StoredKeys(userId), k => k.EndsWith("photo_4.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoredPhoto_ComesBackWithoutLocation()
    {
        var (_, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);
        var photo = Assert.Single(await UploadAsync(client, JpegWithGps()));

        using var response = await client.GetAsync($"/photos/{photo.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var served = Image.Load(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(served.Metadata.ExifProfile);
    }

    [Fact]
    public async Task NamedSlot_ReplacesOnlyThatPhoto_AndKeepsItsName()
    {
        var (userId, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);
        var original = await UploadAsync(client, Jpeg(), Jpeg(), Jpeg());

        var replacement = Assert.Single(await UploadToSlotAsync(client, 2, "Sunday at the lake", Jpeg(40, 90, 200)));

        Assert.Equal(2, replacement.SortOrder);
        Assert.Equal("Sunday at the lake", replacement.Caption);
        Assert.NotEqual(original.Single(p => p.SortOrder == 2).Id, replacement.Id);

        var listed = await ListAsync(client);
        Assert.Equal([1, 2, 3], listed.Select(p => p.SortOrder).ToArray());
        Assert.Equal(original.Single(p => p.SortOrder == 1).Id, listed.Single(p => p.SortOrder == 1).Id);
        Assert.Equal(
            [$"{userId:D}/photo_1.jpg", $"{userId:D}/photo_2.jpg", $"{userId:D}/photo_3.jpg"],
            StoredKeys(userId));
    }

    [Fact]
    public async Task ReplacingASlot_WhenAllFiveAreFull_IsNotOverTheLimit()
    {
        var (_, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);
        await UploadAsync(client, Jpeg(), Jpeg(), Jpeg(), Jpeg(), Jpeg());

        var replaced = Assert.Single(await UploadToSlotAsync(client, 5, null, Jpeg(10, 200, 10)));

        Assert.Equal(5, replaced.SortOrder);
        Assert.Equal(5, (await ListAsync(client)).Count);
    }

    [Fact]
    public async Task SlotOutsideTheRange_IsRefused()
    {
        var (_, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);

        using var form = SingleFileForm(6, null, Jpeg());
        using var response = await client.PostAsync("/photos/Upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("photo_slot_invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Caption_CanBeChangedAndCleared_WithoutReuploading()
    {
        var (_, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);
        var photo = Assert.Single(await UploadToSlotAsync(client, 1, "  First light  ", Jpeg()));
        Assert.Equal("First light", photo.Caption);

        using (var changed = await client.PatchAsJsonAsync($"/photos/{photo.Id}", new { caption = "Second light" }))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }

        Assert.Equal("Second light", Assert.Single(await ListAsync(client)).Caption);

        using (var cleared = await client.PatchAsJsonAsync($"/photos/{photo.Id}", new { caption = "   " }))
        {
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        }

        var after = Assert.Single(await ListAsync(client));
        Assert.Null(after.Caption);
        Assert.Equal(photo.Id, after.Id);
    }

    [Fact]
    public async Task ListedPhotos_CarryALinkTheAppCanShow()
    {
        var (_, token) = await CreateMemberAsync();
        using var client = Client(token);
        await FaceCheck.PassAsync(client);
        await UploadAsync(client, Jpeg());

        var photo = Assert.Single(await ListAsync(client));

        // In tests the storage hands back a data URL; against R2 it is a signed https link.
        Assert.NotNull(photo.Url);
        Assert.StartsWith("data:image/jpeg;base64,", photo.Url);
    }

    private async Task<List<MemberPhotoDto>> UploadToSlotAsync(HttpClient client, int slot, string? caption, byte[] file)
    {
        using var form = SingleFileForm(slot, caption, file);
        using var response = await client.PostAsync("/photos/Upload", form);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Upload failed: {response.StatusCode} {body}");
        return JsonSerializer.Deserialize<ApiResponse<List<MemberPhotoDto>>>(body, Json)!.Data!;
    }

    private static MultipartFormDataContent SingleFileForm(int slot, string? caption, byte[] file)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(content, "photos", "photo.jpg");
        form.Add(new StringContent(slot.ToString()), "slot");
        if (caption is not null)
        {
            form.Add(new StringContent(caption), "caption");
        }

        return form;
    }

    private static async Task<List<MemberPhotoDto>> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/photos/GetAll");
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiResponse<List<MemberPhotoDto>>>(body, Json)!.Data!;
    }

    private async Task<List<MemberPhotoDto>> UploadAsync(HttpClient client, params byte[][] files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "photos", "photo.jpg");
        }

        using var response = await client.PostAsync("/photos/Upload", form);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Upload failed: {response.StatusCode} {body}");
        return JsonSerializer.Deserialize<ApiResponse<List<MemberPhotoDto>>>(body, Json)!.Data!;
    }

    /// <summary>The member's photo keys; the face-check frame (liveness.jpg) shares the folder.</summary>
    private string[] StoredKeys(Guid userId) =>
        factory.Services.GetRequiredService<InMemoryMediaStorage>().Keys
            .Where(k => k.StartsWith($"{userId:D}/photo_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static byte[] Jpeg() => Jpeg(180, 140, 120);

    private static byte[] Jpeg(byte r, byte g, byte b)
    {
        using var image = new Image<Rgb24>(32, 32, new Rgb24(r, g, b));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static byte[] JpegWithGps()
    {
        using var image = new Image<Rgb24>(32, 32, new Rgb24(180, 140, 120));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(12, 1), new Rational(58, 1), new Rational(19, 1)]);
        image.Metadata.ExifProfile = exif;
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

    private async Task<(Guid Id, string Token)> CreateMemberAsync()
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
            Email = $"media-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true,
            AccountKind = AccountKind.Member
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AuthRoles.Member)).Succeeded);

        var account = (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, CancellationToken.None))!;
        var token = sp.GetRequiredService<ITokenService>()
            .CreateAccessToken(account, AuthAudiences.Member, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow);
        return (user.Id, token.AccessToken);
    }
}
