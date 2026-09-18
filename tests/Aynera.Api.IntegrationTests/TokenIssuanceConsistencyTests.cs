using System.Reflection;
using Aynera.Application.Common;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Implementations;
using Aynera.Application.Features.Users.Services.Implementations;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Auth.Statics;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class TokenIssuanceConsistencyTests(AuthApiFactory factory)
{
    [Theory]
    [InlineData(false, false, "deactivate")]
    [InlineData(false, true, "deactivate")]
    [InlineData(true, false, "deactivate")]
    [InlineData(true, true, "deactivate")]
    [InlineData(false, false, "restrict")]
    [InlineData(false, true, "restrict")]
    [InlineData(true, false, "restrict")]
    [InlineData(true, true, "restrict")]
    [InlineData(false, false, "admin")]
    [InlineData(false, true, "admin")]
    [InlineData(true, false, "admin")]
    [InlineData(true, true, "admin")]
    public async Task DisableAndIssuance_SerializeWithoutLeavingLiveSessions(bool refresh, bool issuanceFirst, string operation)
    {
        var account = await SeedAsync(admin: operation == "admin");
        var actor = await SeedAsync(admin: true, super: true);
        var original = refresh ? await LoginAsync(account) : null;
        using var issuingScope = factory.Services.CreateScope();
        var sp = issuingScope.ServiceProvider;
        var gate = new GatedTransaction(sp.GetRequiredService<IWorkflowTransaction>(), issuanceFirst);
        var service = ActivatorUtilities.CreateInstance<AuthService>(sp, gate);
        var issuing = Record.ExceptionAsync(async () =>
        {
            if (refresh) await service.RefreshTokenAsync(new RefreshTokenRequest(original!.RefreshToken), CancellationToken.None);
            else await LoginAsync(service, account);
        });
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var disablingScope = factory.Services.CreateScope();
            var ds = disablingScope.ServiceProvider;
            Task Disable()
            {
                if (operation == "deactivate")
                    return ActivatorUtilities.CreateInstance<AccountLifecycleService>(ds).DeactivateMemberAsync(account.Id, CancellationToken.None);
                var management = ActivatorUtilities.CreateInstance<UserManagementService>(ds);
                return operation == "admin"
                    ? management.DeactivateAdminAsync(actor.Id, account.Id, CancellationToken.None)
                    : management.RestrictMemberAsync(actor.Id, account.Id, CancellationToken.None);
            }
            if (issuanceFirst)
            {
                var disabling = Disable();
                gate.Release.TrySetResult();
                Assert.Null(await issuing.WaitAsync(TimeSpan.FromSeconds(15)));
                await disabling.WaitAsync(TimeSpan.FromSeconds(15));
            }
            else
            {
                await Disable();
                gate.Release.TrySetResult();
                Assert.IsType<AuthException>(await issuing.WaitAsync(TimeSpan.FromSeconds(15)));
            }
        }
        finally { gate.Release.TrySetResult(); }
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AyneraDbContext>();
        var sessions = await db.RefreshSessions.Where(s => s.UserId == account.Id).ToListAsync();
        Assert.Equal((refresh ? 1 : 0) + (issuanceFirst ? 1 : 0), sessions.Count);
        Assert.All(sessions, s => Assert.NotNull(s.RevokedAtUtc));
        var user = await db.Users.SingleAsync(u => u.Id == account.Id);
        Assert.Equal(operation != "restrict", !user.IsActive);
        Assert.Equal(operation == "restrict", user.IsRestricted);
    }

    [Fact]
    public async Task ConcurrentRefresh_CreatesOneReplacement_AndCommitsReuseRevocation()
    {
        var account = await SeedAsync();
        var original = await LoginAsync(account);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<Exception?> Refresh()
        {
            using var scope = factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var gate = new BeforeTransaction(sp.GetRequiredService<IWorkflowTransaction>(), async () =>
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            });
            var service = ActivatorUtilities.CreateInstance<AuthService>(sp, gate);
            return await Record.ExceptionAsync(() => service.RefreshTokenAsync(new RefreshTokenRequest(original.RefreshToken), CancellationToken.None));
        }
        var outcomes = await Task.WhenAll(Refresh(), Refresh()).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(outcomes, e => e is null);
        Assert.Equal("refresh_reuse", Assert.IsType<AuthException>(Assert.Single(outcomes, e => e is not null)).ErrorCode);
        using var check = factory.Services.CreateScope();
        var sessions = await check.ServiceProvider.GetRequiredService<AyneraDbContext>().RefreshSessions
            .Where(s => s.UserId == account.Id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        var parent = Assert.Single(sessions, s => s.ReplacedAtUtc is not null);
        Assert.Equal(Assert.Single(sessions, s => s.Id != parent.Id).Id, parent.ReplacedBySessionId);
        Assert.All(sessions, s => Assert.NotNull(s.RevokedAtUtc));
    }

    [Theory]
    [InlineData(false, "add")]
    [InlineData(false, "cancel")]
    [InlineData(true, "add")]
    [InlineData(true, "replace")]
    [InlineData(true, "cancel")]
    public async Task WriteFailure_RollsBackSessionChanges_AndRetrySucceeds(bool refresh, string stage)
    {
        var account = await SeedAsync();
        var original = refresh ? await LoginAsync(account) : null;
        var audit = new CapturingAudit();
        using (var scope = factory.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource())
        {
            var sp = scope.ServiceProvider;
            var proxy = DispatchProxy.Create<IRefreshSessionRepository, AccountDeactivationConsistencyTests.FailureAfterWrite>();
            var settings = (AccountDeactivationConsistencyTests.FailureAfterWrite)(object)proxy;
            settings.Inner = sp.GetRequiredService<IRefreshSessionRepository>();
            settings.Method = stage == "replace" || (refresh && stage == "cancel")
                ? nameof(IRefreshSessionRepository.MarkReplacedAsync) : nameof(IRefreshSessionRepository.AddAsync);
            settings.Cancellation = stage == "cancel" ? cancellation : null;
            var service = ActivatorUtilities.CreateInstance<AuthService>(sp, proxy, audit);
            Task Issue() => refresh
                ? service.RefreshTokenAsync(new RefreshTokenRequest(original!.RefreshToken), cancellation.Token)
                : service.LoginWithPasswordAsync(new MemberPasswordLoginRequest(account.Phone, "secret12"), cancellation.Token);
            if (stage == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Issue);
            else await Assert.ThrowsAsync<InvalidOperationException>(Issue);
        }
        Assert.Empty(audit.Events);
        using (var check = factory.Services.CreateScope())
        {
            var sessions = await check.ServiceProvider.GetRequiredService<AyneraDbContext>().RefreshSessions
                .Where(s => s.UserId == account.Id).ToListAsync();
            Assert.Equal(refresh ? 1 : 0, sessions.Count);
            Assert.All(sessions, s => { Assert.Null(s.ReplacedAtUtc); Assert.Null(s.RevokedAtUtc); });
        }
        using (var retry = factory.Services.CreateScope())
        {
            var service = ActivatorUtilities.CreateInstance<AuthService>(retry.ServiceProvider);
            if (refresh) await service.RefreshTokenAsync(new RefreshTokenRequest(original!.RefreshToken), CancellationToken.None);
            else await LoginAsync(service, account);
        }
        using var verify = factory.Services.CreateScope();
        Assert.Equal(refresh ? 2 : 1, await verify.ServiceProvider.GetRequiredService<AyneraDbContext>()
            .RefreshSessions.CountAsync(s => s.UserId == account.Id));
    }

    [Theory]
    [InlineData("expired", "refresh_expired")]
    [InlineData("inactive", "account_deactivated")]
    [InlineData("restricted", "account_restricted")]
    [InlineData("wrong-kind", "invalid_account")]
    public async Task RefreshDenial_CommitsRevocation(string state, string expected)
    {
        var account = await SeedAsync();
        var original = await LoginAsync(account);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
            if (state == "expired") await db.RefreshSessions.Where(s => s.UserId == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
            else if (state == "inactive") await db.Users.Where(u => u.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
            else if (state == "restricted") await db.Users.Where(u => u.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsRestricted, true));
            else await db.Users.Where(u => u.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountKind, AccountKind.Admin));
        }
        using (var scope = factory.Services.CreateScope())
        {
            var service = ActivatorUtilities.CreateInstance<AuthService>(scope.ServiceProvider);
            var error = await Assert.ThrowsAsync<AuthException>(() => service.RefreshTokenAsync(new RefreshTokenRequest(original.RefreshToken), CancellationToken.None));
            Assert.Equal(expected, error.ErrorCode);
        }
        using var check = factory.Services.CreateScope();
        var session = await check.ServiceProvider.GetRequiredService<AyneraDbContext>().RefreshSessions.SingleAsync(s => s.UserId == account.Id);
        Assert.NotNull(session.RevokedAtUtc);
        Assert.Null(session.ReplacedAtUtc);
    }

    private sealed record Account(Guid Id, string Phone, bool Admin);

    private async Task<Account> SeedAsync(bool admin = false, bool super = false)
    {
        using var scope = factory.Services.CreateScope();
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser { Id = Guid.NewGuid(), UserName = phone, PhoneNumber = phone,
            AccountKind = admin ? AccountKind.Admin : AccountKind.Member, IsSuperAdmin = super };
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user, "secret12")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, admin ? AuthRoles.Admin : AuthRoles.Member)).Succeeded);
        return new Account(user.Id, phone, admin);
    }

    private async Task<TokenResponse> LoginAsync(Account account)
    {
        using var scope = factory.Services.CreateScope();
        return await LoginAsync(ActivatorUtilities.CreateInstance<AuthService>(scope.ServiceProvider), account);
    }

    private static Task<TokenResponse> LoginAsync(AuthService service, Account account) => account.Admin
        ? service.LoginWithPasswordAsync(new AdminPasswordLoginRequest(account.Phone, "secret12"), CancellationToken.None)
        : service.LoginWithPasswordAsync(new MemberPasswordLoginRequest(account.Phone, "secret12"), CancellationToken.None);

    private sealed class GatedTransaction(IWorkflowTransaction inner, bool inside) : IWorkflowTransaction
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task Pause()
        { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        public async Task<T> ExecuteAsync<T>(IReadOnlyList<string> keys, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
        {
            if (!inside) await Pause();
            return await inner.ExecuteAsync(keys, async token =>
            { if (inside) await Pause(); return await operation(token); }, ct);
        }
    }

    private sealed class BeforeTransaction(IWorkflowTransaction inner, Func<Task> before) : IWorkflowTransaction
    {
        public async Task<T> ExecuteAsync<T>(IReadOnlyList<string> keys, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
        { await before(); return await inner.ExecuteAsync(keys, operation, ct); }
    }

    private sealed class CapturingAudit : IAuditWriter
    {
        public List<AuditEventWriteModel> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteModel model, CancellationToken ct)
        { Events.Add(model); return Task.CompletedTask; }
    }
}
