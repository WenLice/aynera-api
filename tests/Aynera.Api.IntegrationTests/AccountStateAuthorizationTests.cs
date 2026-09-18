using System.Net;
using System.Net.Http.Headers;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Statics;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class AccountStateAuthorizationTests(AuthApiFactory factory)
{
    [Theory]
    [InlineData(false, "inactive")]
    [InlineData(false, "restricted")]
    [InlineData(false, "deleted")]
    [InlineData(false, "missing")]
    [InlineData(false, "wrong-kind")]
    [InlineData(true, "inactive")]
    [InlineData(true, "restricted")]
    [InlineData(true, "deleted")]
    [InlineData(true, "missing")]
    [InlineData(true, "wrong-kind")]
    public async Task SameSignedAccessToken_StopsWorkingAfterAccountStateChanges(bool admin, string state)
    {
        var (id, token) = await CreateAccountAsync(admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var routes = admin
            ? new[] { "/admins/me", "/members/GetAll", "/suggestions/GetAll", "/feedback/GetAll", "/audit/events/GetAll", "/early-access/signups/GetAll" }
            : new[] { "/members/me", "/photos/GetAll" };
        foreach (var route in routes)
        {
            using var before = await client.GetAsync(route);
            Assert.True(before.StatusCode == HttpStatusCode.OK, $"{route}: {before.StatusCode}");
        }
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AyneraDbContext>();
            if (state == "inactive")
            {
                if (admin)
                {
                    var (actor, _) = await CreateAccountAsync(admin: true, super: true);
                    await sp.GetRequiredService<IUserManagementService>().DeactivateAdminAsync(actor, id, CancellationToken.None);
                }
                else await sp.GetRequiredService<IAccountLifecycleService>().DeactivateMemberAsync(id, CancellationToken.None);
            }
            else if (state == "restricted" && !admin)
            {
                var (actor, _) = await CreateAccountAsync(admin: true, super: true);
                await sp.GetRequiredService<IUserManagementService>().RestrictMemberAsync(actor, id, CancellationToken.None);
            }
            else if (state == "missing") await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync();
            else if (state == "deleted") await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDeleted, true));
            else if (state == "restricted") await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsRestricted, true));
            else await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountKind, admin ? AccountKind.Member : AccountKind.Admin));
        }
        foreach (var route in routes)
        {
            using var denied = await client.GetAsync(route);
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, $"{route}: {denied.StatusCode}");
        }
        if (!admin)
        {
            // Middleware must reject mutations before parsing their bodies or invoking media services.
            using var upload = await client.PostAsync("/photos/Upload", null);
            Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
            using var password = await client.PostAsync("/members/me/password", null);
            Assert.Equal(HttpStatusCode.Forbidden, password.StatusCode);
        }
    }

    [Fact]
    public async Task InactiveMember_CanReactivate_WithSameToken_ThenUseNormalApis()
    {
        var (id, token) = await CreateAccountAsync(admin: false);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>().DeactivateMemberAsync(id, CancellationToken.None);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var denied = await client.GetAsync("/photos/GetAll");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var activated = await client.PostAsync("/members/me/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        using var allowed = await client.GetAsync("/photos/GetAll");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberDeletion_RemainsAvailableWhileInactiveOrRestricted(bool restricted)
    {
        var (id, token) = await CreateAccountAsync(admin: false);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().Users.Where(u => u.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false).SetProperty(u => u.IsRestricted, restricted));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (restricted)
        {
            using var reactivation = await client.PostAsync("/members/me/reactivate", null);
            Assert.Equal(HttpStatusCode.Forbidden, reactivation.StatusCode);
        }
        using var deleted = await client.DeleteAsync("/members/me");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var rejected = await client.PostAsync("/members/me/reactivate", null);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    private async Task<(Guid Id, string Token)> CreateAccountAsync(bool admin, bool super = false)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser { Id = Guid.NewGuid(), UserName = phone, PhoneNumber = phone,
            AccountKind = admin ? AccountKind.Admin : AccountKind.Member, IsSuperAdmin = super };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, admin ? AuthRoles.Admin : AuthRoles.Member)).Succeeded);
        var account = (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, CancellationToken.None))!;
        var token = sp.GetRequiredService<ITokenService>().CreateAccessToken(account,
            admin ? AuthAudiences.Admin : AuthAudiences.Member, Guid.NewGuid(), "pwd", DateTimeOffset.UtcNow);
        return (user.Id, token.AccessToken);
    }
}
