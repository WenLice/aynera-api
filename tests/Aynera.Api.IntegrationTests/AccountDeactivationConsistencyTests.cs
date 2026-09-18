using System.Reflection;
using Aynera.Application.Common;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Microsoft.Extensions.Options;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Application.Features.Videos.Repositories;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Application.Features.Users.Services.Implementations;
using Aynera.Domain.Audit.Records;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Requests;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class AccountDeactivationConsistencyTests(AuthApiFactory factory)
{
    [Theory]
    [InlineData("account")]
    [InlineData("sessions")]
    [InlineData("cancellation")]
    public async Task Failure_RollsBackAccountAndSessions_ThenRetrySucceeds(string failure)
    {
        var id = await CreateMemberAsync();
        var otherId = await CreateMemberAsync();
        var audit = new CapturingAudit();
        using (var scope = factory.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource())
        {
            var sp = scope.ServiceProvider;
            T Store<T>(string stage, string method) where T : class
            {
                var inner = sp.GetRequiredService<T>();
                if (failure != stage && !(failure == "cancellation" && stage == "sessions")) return inner;
                var proxy = DispatchProxy.Create<T, FailureAfterWrite>();
                var settings = (FailureAfterWrite)(object)proxy;
                settings.Inner = inner;
                settings.Method = method;
                settings.Cancellation = failure == "cancellation" ? cancellation : null;
                return proxy;
            }
            var service = new AccountLifecycleService(
                Store<IUserRepository>("account", nameof(IUserRepository.DeactivateMemberAsync)),
                sp.GetRequiredService<IMemberProfileRepository>(), sp.GetRequiredService<IMemberPhotoRepository>(),
                sp.GetRequiredService<IIntroductionVideoRepository>(),
                Store<IRefreshSessionRepository>("sessions", nameof(IRefreshSessionRepository.RevokeAllForUserAsync)),
                audit, sp.GetRequiredService<ILogger<AccountLifecycleService>>(),
                sp.GetRequiredService<IWorkflowTransaction>(), sp.GetRequiredService<IVerificationEmailQueue>(),
                sp.GetRequiredService<IOtpChallengeRepository>(), sp.GetRequiredService<ISmsService>(),
                sp.GetRequiredService<IEmailService>(),
                sp.GetRequiredService<IOptions<OtpOptions>>(),
                sp.GetRequiredService<IOptions<JwtOptions>>());
            if (failure == "cancellation")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeactivateMemberAsync(id, cancellation.Token));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeactivateMemberAsync(id, cancellation.Token));
        }
        Assert.Empty(audit.Events);
        await AssertStateAsync(id, active: true);
        await AssertStateAsync(otherId, active: true);

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>().DeactivateMemberAsync(id, CancellationToken.None);
        await AssertStateAsync(id, active: false);
        await AssertStateAsync(otherId, active: true);
    }

    private async Task<Guid> CreateMemberAsync()
    {
        using var scope = factory.Services.CreateScope();
        var request = new CreateMemberRequest("9" + Random.Shared.NextInt64(100_000_000, 999_999_999),
            "Ada Lovelace", Gender.Female, new DateOnly(1990, 5, 15), "Mumbai",
            $"deactivation-{Guid.NewGuid():N}@example.com", Password: "secret12");
        var account = await scope.ServiceProvider.GetRequiredService<IRegistrationService>().RegisterAsync(request, CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        for (var device = 0; device < 2; device++)
            db.RefreshSessions.Add(new RefreshSession { Id = Guid.NewGuid(), UserId = account.Id, Audience = "member",
                TokenHash = Guid.NewGuid().ToString("N"), FamilyId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task AssertStateAsync(Guid id, bool active)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == id);
        Assert.Equal(active, user.IsActive);
        Assert.Equal(!active, user.DeactivatedAtUtc.HasValue);
        Assert.False(user.IsDeleted);
        Assert.False(user.IsRestricted);
        var sessions = await db.RefreshSessions.IgnoreQueryFilters().Where(x => x.UserId == id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => { Assert.Equal(!active, session.RevokedAtUtc.HasValue); Assert.False(session.IsDeleted); });
        Assert.True(await db.MemberProfiles.AnyAsync(x => x.UserId == id));
        Assert.True(await db.VerificationEmailDeliveries.AnyAsync(x => x.UserId == id));
        Assert.Equal(active ? 0 : 1, await db.AuditEvents.CountAsync(x => x.SubjectUserId == id && x.Action == AuditActions.MemberDeactivated));
    }

    public class FailureAfterWrite : DispatchProxy
    {
        public object Inner { get; set; } = null!;
        public string Method { get; set; } = "";
        public CancellationTokenSource? Cancellation { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Inner, args);
            return method.Name == Method ? FailAsync((Task)result!) : result;
        }
        private async Task FailAsync(Task write)
        {
            await write;
            if (Cancellation is not null) { Cancellation.Cancel(); Cancellation.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("Injected deactivation failure after persistence.");
        }
    }

    private sealed class CapturingAudit : IAuditWriter
    {
        public List<AuditEventWriteModel> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteModel model, CancellationToken ct)
        { Events.Add(model); return Task.CompletedTask; }
    }
}
