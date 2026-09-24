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
using Aynera.Domain.Settings.Responses;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// The member settings table through the real endpoints: notification switches, pause, and field
/// visibility — which is the same stored choice whether it is changed from Settings or from the
/// profile and answer pages.
/// </summary>
[Collection("Integration")]
public sealed class MemberSettingsEndpointsTests(AuthApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NothingSet_ReturnsDefaults()
    {
        using var client = Client(await CreateMemberTokenAsync());

        var settings = await GetSettingsAsync(client);

        Assert.Null(settings.NotifyIntroductions);
        Assert.Null(settings.NotifyReplies);
        Assert.Null(settings.NotifyWeekendSurprise);
        Assert.False(settings.IntroductionsPaused);
        Assert.Null(settings.PausedAtUtc);
        Assert.Empty(settings.Visibility);
    }

    [Fact]
    public async Task Changes_ArePartial_AndThePauseKeepsWhenItBegan()
    {
        using var client = Client(await CreateMemberTokenAsync());

        var paused = await PatchSettingsAsync(client, new { introductionsPaused = true, notifyReplies = false });
        Assert.True(paused.IntroductionsPaused);
        Assert.NotNull(paused.PausedAtUtc);
        Assert.False(paused.NotifyReplies);

        // Pausing again, or changing something else, does not restart the clock.
        var again = await PatchSettingsAsync(client, new { introductionsPaused = true, notifyIntroductions = true });
        Assert.Equal(paused.PausedAtUtc, again.PausedAtUtc);
        Assert.False(again.NotifyReplies);
        Assert.True(again.NotifyIntroductions);

        var resumed = await PatchSettingsAsync(client, new { introductionsPaused = false });
        Assert.False(resumed.IntroductionsPaused);
        Assert.Null(resumed.PausedAtUtc);
        Assert.True(resumed.NotifyIntroductions);
    }

    [Fact]
    public async Task EmptyChange_OrAnInvalidFieldName_IsRefused()
    {
        using var client = Client(await CreateMemberTokenAsync());

        using var empty = await client.PatchAsJsonAsync("/members/me/settings", new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var bad = await client.PatchAsJsonAsync(
            "/members/me/settings", new { visibility = new Dictionary<string, bool> { ["../gender"] = false } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    /// <summary>
    /// The gender's and each answer's show-on-profile switch are one stored choice: set from the
    /// registration pages, they read back in Settings; changed in Settings, the pages read the change.
    /// </summary>
    [Fact]
    public async Task Visibility_IsOneChoice_WhicheverPageChangesIt()
    {
        using var client = Client(await CreateMemberTokenAsync());

        await PatchRegistrationAsync(client, new
        {
            name = "Riya", gender = "Female", genderIsPublic = false, dateOfBirth = "1996-04-12",
            hometown = "Pune", city = "Bangalore",
            lifestyle = new { drink = new { option = "Sometimes", @public = false }, diet = new { option = "Vegetarian", @public = true } },
        });

        var settings = await GetSettingsAsync(client);
        Assert.False(settings.Visibility["gender"]);
        Assert.False(settings.Visibility["lifestyle.drink"]);
        Assert.True(settings.Visibility["lifestyle.diet"]);

        await PatchSettingsAsync(client, new { visibility = new Dictionary<string, bool> { ["gender"] = true, ["lifestyle.drink"] = true } });

        var progress = await GetRegistrationAsync(client);
        Assert.True(progress.Profile!.GenderIsPublic);
        Assert.True(progress.ProfileAnswers!.Lifestyle["drink"].Public);
        Assert.True(progress.ProfileAnswers.Lifestyle["diet"].Public);

        // A notifications answer on the registration page leaves the visibility alone.
        await PatchRegistrationAsync(client, new { interestedIn = "Male", minAge = 24, maxAge = 32, track = "Intent", outcome = "Prospect" });
        await PatchRegistrationAsync(client, new { notificationsOn = true });
        var after = await GetSettingsAsync(client);
        Assert.True(after.NotifyIntroductions);
        Assert.True(after.NotifyWeekendSurprise);
        Assert.True(after.Visibility["gender"]);
    }

    /// <summary>
    /// "Prefer not to say" is stored as the answer, like any option, but it is never published, so
    /// saving it writes no visibility entry and leaves an existing one exactly as it was.
    /// </summary>
    [Fact]
    public async Task PreferNotToSay_IsStored_AndLeavesVisibilityAlone()
    {
        using var client = Client(await CreateMemberTokenAsync());

        await PatchRegistrationAsync(client, new
        {
            beliefs = new { children = new { option = "Want them", @public = false } },
        });
        Assert.False((await GetSettingsAsync(client)).Visibility["beliefs.children"]);

        await PatchRegistrationAsync(client, new
        {
            beliefs = new
            {
                children = new { option = "Prefer not to say", @public = true },
                family = new { option = "Prefer not to say", @public = true },
            },
        });

        var settings = await GetSettingsAsync(client);
        Assert.False(settings.Visibility["beliefs.children"]);
        Assert.False(settings.Visibility.ContainsKey("beliefs.family"));

        var progress = await GetRegistrationAsync(client);
        Assert.Equal("Prefer not to say", progress.ProfileAnswers!.Beliefs["children"].Option);
        Assert.Equal("Prefer not to say", progress.ProfileAnswers.Beliefs["family"].Option);
    }

    /// <summary>
    /// The migration copies every visibility choice out of the old places — a hidden gender and a
    /// hidden answer included — before it removes them.
    /// </summary>
    [Fact]
    public async Task Migration_CopiesHiddenGenderAndHiddenAnswers_IntoSettings()
    {
        const string before = "20260923172824_AddLivenessSessions";
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AyneraDbContext>();
        var migrator = db.GetService<IMigrator>();
        var city = await db.EarlyAccessCities.SingleAsync(c => c.Name == "Bangalore");
        var id = await CreateUserAsync(sp);

        try
        {
            await migrator.MigrateAsync(before);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "MemberProfiles" ("UserId", "Name", "Gender", "GenderIsPublic", "DateOfBirth", "City", "CityId", "Hometown", "CreatedAtUtc", "IsDeleted")
                VALUES ({0}, 'Legacy', 'Female', FALSE, '1995-02-20', 'Bangalore', {1}, 'Mysore', now(), FALSE);
                INSERT INTO "MemberProfileAnswers" ("UserId", "Lifestyle", "Beliefs", "Vibe", "CreatedAtUtc", "IsDeleted")
                VALUES ({0},
                        '{{"drink":{{"option":"No","public":false}},"diet":{{"option":"Vegan","public":true}}}}'::jsonb,
                        '{{"faith":{{"option":"Practising","public":false}},"children":{{"option":"Prefer not to say","public":true}}}}'::jsonb,
                        '[]'::jsonb, now(), FALSE);
                """,
                id, city.Id);

            // Straight forward from here — the replay helper steps back past the migration that
            // added GenderIsPublic, which would reset the very flag under test.
            await migrator.MigrateAsync();

            var visibility = await db.Database
                .SqlQueryRaw<string>("""SELECT "Visibility"::text AS "Value" FROM "MemberSettings" WHERE "UserId" = {0}""", id)
                .SingleAsync();
            var map = JsonSerializer.Deserialize<Dictionary<string, bool>>(visibility)!;
            Assert.False(map["gender"]);
            Assert.False(map["lifestyle.drink"]);
            Assert.True(map["lifestyle.diet"]);
            Assert.False(map["beliefs.faith"]);
            // Never published, so no visibility entry — and the answer itself is kept.
            Assert.False(map.ContainsKey("beliefs.children"));
            var beliefs = await db.Database
                .SqlQueryRaw<string>("""SELECT "Beliefs"::text AS "Value" FROM "MemberProfileAnswers" WHERE "UserId" = {0}""", id)
                .SingleAsync();
            Assert.Contains("Prefer not to say", beliefs);

            var lifestyle = await db.Database
                .SqlQueryRaw<string>("""SELECT "Lifestyle"::text AS "Value" FROM "MemberProfileAnswers" WHERE "UserId" = {0}""", id)
                .SingleAsync();
            Assert.DoesNotContain("public", lifestyle);
            Assert.Contains("No", lifestyle);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM "MemberSettings" WHERE "UserId" = {0};
                DELETE FROM "MemberProfileAnswers" WHERE "UserId" = {0};
                DELETE FROM "MemberProfiles" WHERE "UserId" = {0};
                """,
                id);
            await MigrationReplay.RestoreLatestAsync(db);
        }
    }

    private static async Task<MemberSettingsDto> GetSettingsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/members/me/settings");
        return await ReadAsync<MemberSettingsDto>(response);
    }

    private static async Task<MemberSettingsDto> PatchSettingsAsync(HttpClient client, object change)
    {
        using var response = await client.PatchAsJsonAsync("/members/me/settings", change);
        return await ReadAsync<MemberSettingsDto>(response);
    }

    private static async Task PatchRegistrationAsync(HttpClient client, object page)
    {
        using var response = await client.PatchAsJsonAsync("/members/me/registration", page);
        await ReadAsync<RegistrationProgressDto>(response);
    }

    private static async Task<RegistrationProgressDto> GetRegistrationAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/members/me/registration");
        return await ReadAsync<RegistrationProgressDto>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.RequestMessage?.RequestUri}: {response.StatusCode} {body}");
        return JsonSerializer.Deserialize<ApiResponse<T>>(body, Json)!.Data!;
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CreateUserAsync(IServiceProvider sp)
    {
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            Email = $"settings-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true,
            AccountKind = AccountKind.Member
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AuthRoles.Member)).Succeeded);
        return user.Id;
    }

    private async Task<string> CreateMemberTokenAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var id = await CreateUserAsync(sp);
        var account = (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(id, CancellationToken.None))!;
        return sp.GetRequiredService<ITokenService>()
            .CreateAccessToken(account, AuthAudiences.Member, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow)
            .AccessToken;
    }
}
