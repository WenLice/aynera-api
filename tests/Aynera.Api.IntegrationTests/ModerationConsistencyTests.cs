using System.Reflection;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Users.Services.Implementations;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class ModerationConsistencyTests(AuthApiFactory factory)
{
    [Theory]
    [InlineData(false, "account")]
    [InlineData(false, "sessions")]
    [InlineData(false, "cancellation")]
    [InlineData(true, "account")]
    [InlineData(true, "sessions")]
    [InlineData(true, "cancellation")]
    public async Task Failure_RollsBackStateAndSessions_ThenRetrySucceeds(bool admin, string failure)
    {
        var actor = await SeedAsync(admin: true, super: true);
        var target = await SeedAsync(admin);
        var other = await SeedAsync(admin);
        var audit = new CapturingAudit();
        using (var scope = factory.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource())
        {
            var sp = scope.ServiceProvider;
            object store = failure == "account"
                ? FailAfter<IUserRepository>(sp, admin ? nameof(IUserRepository.DeactivateMemberAsync)
                    : nameof(IUserRepository.RestrictMemberAsync), null)
                : FailAfter<IRefreshSessionRepository>(sp, nameof(IRefreshSessionRepository.RevokeAllForUserAsync),
                    failure == "cancellation" ? cancellation : null);
            var service = ActivatorUtilities.CreateInstance<UserManagementService>(sp, store, audit);
            if (failure == "cancellation")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DisableAsync(service, admin, actor, target, cancellation.Token));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => DisableAsync(service, admin, actor, target));
        }
        Assert.Empty(audit.Events);
        await AssertStateAsync(target, active: true, restricted: false, revoked: false);
        await AssertStateAsync(other, active: true, restricted: false, revoked: false);
        using (var scope = factory.Services.CreateScope())
            await DisableAsync(scope.ServiceProvider.GetRequiredService<IUserManagementService>(), admin, actor, target);
        await AssertStateAsync(target, active: !admin, restricted: !admin, revoked: true);
        await AssertStateAsync(other, active: true, restricted: false, revoked: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedDisable_RepairsLegacySessions_WithoutDuplicateAudit(bool admin)
    {
        var actor = await SeedAsync(admin: true, super: true);
        var target = await SeedAsync(admin, active: !admin, restricted: !admin);
        var audit = new CapturingAudit();
        for (var i = 0; i < 2; i++)
        {
            using var scope = factory.Services.CreateScope();
            var service = ActivatorUtilities.CreateInstance<UserManagementService>(scope.ServiceProvider, audit);
            await DisableAsync(service, admin, actor, target);
        }
        await AssertStateAsync(target, active: !admin, restricted: !admin, revoked: true);
        Assert.Empty(audit.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enable_RollsBackOnFailure_AndDoesNotRestoreSessions(bool admin)
    {
        var actor = await SeedAsync(admin: true, super: true);
        var target = await SeedAsync(admin, active: false, restricted: !admin, revoked: true);
        var audit = new CapturingAudit();
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var users = FailAfter<IUserRepository>(sp, admin ? nameof(IUserRepository.ActivateMemberAsync)
                : nameof(IUserRepository.UnrestrictMemberAsync), null);
            var service = ActivatorUtilities.CreateInstance<UserManagementService>(sp, users, audit);
            await Assert.ThrowsAsync<InvalidOperationException>(() => EnableAsync(service, admin, actor, target));
        }
        Assert.Empty(audit.Events);
        await AssertStateAsync(target, active: false, restricted: !admin, revoked: true);
        using (var scope = factory.Services.CreateScope())
        {
            var service = ActivatorUtilities.CreateInstance<UserManagementService>(scope.ServiceProvider, audit);
            await EnableAsync(service, admin, actor, target);
            await EnableAsync(service, admin, actor, target);
        }
        // Unrestriction must preserve the independent self-deactivation timestamp.
        await AssertStateAsync(target, active: admin, restricted: false, revoked: true);
        Assert.Single(audit.Events);
    }

    [Fact]
    public async Task ConcurrentSuperAdmins_CannotDeactivateEachOther()
    {
        var first = await SeedAsync(admin: true, super: true);
        var second = await SeedAsync(admin: true, super: true);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<Exception?> Attempt(Guid actor, Guid target)
        {
            using var scope = factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            // Mimic a request that has already read its actor before waiting on the workflow.
            await sp.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(actor.ToString());
            if (Interlocked.Increment(ref arrivals) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return await Record.ExceptionAsync(() => sp.GetRequiredService<IUserManagementService>()
                .DeactivateAdminAsync(actor, target, CancellationToken.None));
        }
        var results = await Task.WhenAll(Attempt(first, second), Attempt(second, first)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, error => error is null);
        Assert.Equal("super_admin_required", Assert.IsType<AuthException>(Assert.Single(results, error => error is not null)).ErrorCode);
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AyneraDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => (u.Id == first || u.Id == second) && u.IsActive));
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => (a.SubjectUserId == first || a.SubjectUserId == second)
            && a.Action == AuditActions.AdminDeactivated));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("restricted")]
    [InlineData("regular")]
    [InlineData("deleted")]
    public async Task IneligibleActor_CannotMutateMember(string kind)
    {
        var actor = await SeedAsync(admin: true, super: kind != "regular", active: kind != "inactive", restricted: kind == "restricted");
        var target = await SeedAsync(admin: false);
        if (kind == "deleted")
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().Users.Where(u => u.Id == actor)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDeleted, true));
        }
        using (var scope = factory.Services.CreateScope())
            await Assert.ThrowsAsync<AuthException>(() => scope.ServiceProvider.GetRequiredService<IUserManagementService>()
                .RestrictMemberAsync(actor, target, CancellationToken.None));
        await AssertStateAsync(target, active: true, restricted: false, revoked: false);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("email")]
    [InlineData("id")]
    public async Task IdentifierRead_BeforeCompetingActivation_DoesNotLeaveStaleIdentity(string lookup)
    {
        var id = await SeedAsync(admin: false, active: false, revoked: true);
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var snapshot = await sp.GetRequiredService<AyneraDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
        var users = sp.GetRequiredService<IUserRepository>();
        var before = lookup switch
        {
            "phone" => await users.FindByPhoneAsync(snapshot.UserName!, CancellationToken.None),
            "email" => await users.FindByEmailAsync(snapshot.Email!, CancellationToken.None),
            _ => await users.FindByIdAsync(id, CancellationToken.None)
        };
        Assert.False(before!.IsActive);
        using (var competing = factory.Services.CreateScope())
            await competing.ServiceProvider.GetRequiredService<IAccountLifecycleService>().ActivateMemberAsync(id, CancellationToken.None);
        // This second operation must see the first commit, not update an old concurrency stamp.
        await sp.GetRequiredService<IAccountLifecycleService>().ActivateMemberAsync(id, CancellationToken.None);
        await AssertStateAsync(id, active: true, restricted: false, revoked: true);
    }

    private async Task<Guid> SeedAsync(bool admin, bool super = false, bool active = true, bool restricted = false, bool revoked = false)
    {
        using var scope = factory.Services.CreateScope();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"moderation-{Guid.NewGuid():N}@example.com",
            Email = $"moderation-{Guid.NewGuid():N}@example.com",
            AccountKind = admin ? AccountKind.Admin : AccountKind.Member, IsSuperAdmin = super,
            IsActive = active, DeactivatedAtUtc = active ? null : DateTimeOffset.UtcNow,
            IsRestricted = restricted, RestrictedAtUtc = restricted ? DateTimeOffset.UtcNow : null };
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(user);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        for (var i = 0; i < 2; i++)
            db.RefreshSessions.Add(new RefreshSession { Id = Guid.NewGuid(), UserId = user.Id,
                Audience = admin ? "admin" : "member", TokenHash = Guid.NewGuid().ToString("N"), FamilyId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                RevokedAtUtc = revoked ? DateTimeOffset.UtcNow : null });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task AssertStateAsync(Guid id, bool active, bool restricted, bool revoked)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        Assert.Equal(active, user.IsActive);
        Assert.Equal(!active, user.DeactivatedAtUtc.HasValue);
        Assert.Equal(restricted, user.IsRestricted);
        Assert.Equal(restricted, user.RestrictedAtUtc.HasValue);
        var sessions = await db.RefreshSessions.Where(s => s.UserId == id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(revoked, s.RevokedAtUtc.HasValue));
    }

    private static async Task DisableAsync(IUserManagementService service, bool admin, Guid actor, Guid target, CancellationToken ct = default)
    {
        if (admin) await service.DeactivateAdminAsync(actor, target, ct);
        else await service.RestrictMemberAsync(actor, target, ct);
    }

    private static async Task EnableAsync(IUserManagementService service, bool admin, Guid actor, Guid target)
    {
        if (admin) await service.ActivateAdminAsync(actor, target, CancellationToken.None);
        else await service.UnrestrictMemberAsync(actor, target, CancellationToken.None);
    }

    private static T FailAfter<T>(IServiceProvider sp, string method, CancellationTokenSource? cancellation) where T : class
    {
        var proxy = DispatchProxy.Create<T, AccountDeactivationConsistencyTests.FailureAfterWrite>();
        var settings = (AccountDeactivationConsistencyTests.FailureAfterWrite)(object)proxy;
        settings.Inner = sp.GetRequiredService<T>();
        settings.Method = method;
        settings.Cancellation = cancellation;
        return proxy;
    }

    private sealed class CapturingAudit : IAuditWriter
    {
        public List<AuditEventWriteModel> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteModel model, CancellationToken ct)
        { Events.Add(model); return Task.CompletedTask; }
    }
}
