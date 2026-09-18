using System.Collections.Concurrent;
using System.Reflection;
using Aynera.Application.Common;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.EarlyAccess.Repositories;
using Aynera.Application.Features.Profiles.Repositories;
using Aynera.Application.Features.Users.Services.Implementations;
using Aynera.Application.Features.Users.Services.Interfaces;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Infrastructure.Services;
using Aynera.Persistence;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class RegistrationConsistencyTests(AuthApiFactory factory)
{
    private static CreateMemberRequest Request() => new(
        "9" + Random.Shared.NextInt64(100_000_000, 999_999_999), "Ada", "Lovelace", Gender.Female,
        new DateOnly(1990, 5, 15), "Mumbai", $"registration-{Guid.NewGuid():N}@example.com", null, "secret12");

    [Theory]
    [InlineData("identity")]
    [InlineData("profile")]
    [InlineData("queue")]
    [InlineData("cancellation")]
    public async Task FailedRegistration_RollsBackEveryWrite_AndCanBeRetried(string stage)
    {
        var request = Request();
        Guid createdId;
        using (var scope = factory.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource())
        {
            var sp = scope.ServiceProvider;
            var proxy = DispatchProxy.Create<IUserRepository, IdentityWriteObserver>();
            var observer = (IdentityWriteObserver)proxy;
            observer.Inner = sp.GetRequiredService<IUserRepository>();
            observer.Fail = stage == "identity";
            var profiles = sp.GetRequiredService<IMemberProfileRepository>();
            var queue = sp.GetRequiredService<IVerificationEmailQueue>();
            var service = new RegistrationService(proxy,
                stage == "profile" ? new FailingProfile(profiles) : profiles,
                sp.GetRequiredService<IEarlyAccessCityRepository>(),
                sp.GetRequiredService<IWorkflowTransaction>(),
                stage is "queue" or "cancellation" ? new FailingQueue(queue, stage == "cancellation" ? cancellation : null) : queue,
                sp.GetRequiredService<IMapper>(), sp.GetRequiredService<IAuditWriter>(),
                sp.GetRequiredService<IOptions<EmailOptions>>(), sp.GetRequiredService<ILogger<RegistrationService>>(),
                sp.GetRequiredService<IOtpChallengeRepository>(), sp.GetRequiredService<ISmsService>(),
                sp.GetRequiredService<IEmailService>(), sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<IOptions<OtpOptions>>(), sp.GetRequiredService<IOptions<JwtOptions>>());

            if (stage == "cancellation")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RegisterAsync(request, cancellation.Token));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(request, cancellation.Token));
            createdId = observer.CreatedId;
            Assert.NotEqual(Guid.Empty, createdId);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
            Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == createdId));
            Assert.False(await db.UserRoles.AnyAsync(x => x.UserId == createdId));
            Assert.False(await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(x => x.UserId == createdId));
            Assert.False(await db.VerificationEmailDeliveries.AnyAsync(x => x.UserId == createdId));
            var account = await scope.ServiceProvider.GetRequiredService<IRegistrationService>().RegisterAsync(request, CancellationToken.None);
            Assert.NotEqual(createdId, account.Id);
            Assert.True(await db.UserRoles.AnyAsync(x => x.UserId == account.Id));
            Assert.True(await db.MemberProfiles.AnyAsync(x => x.UserId == account.Id));
            Assert.True(await db.VerificationEmailDeliveries.AnyAsync(x => x.UserId == account.Id));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentDuplicateRegistration_HasOneWinner(bool samePhone)
    {
        var first = Request();
        var second = samePhone ? Request() with { Phone = first.Phone }
            : Request() with { Email = first.Email.ToUpperInvariant() };
        async Task<(AuthAccountDto? Account, AuthException? Error)> Register(CreateMemberRequest request)
        {
            using var scope = factory.Services.CreateScope();
            try { return (await scope.ServiceProvider.GetRequiredService<IRegistrationService>().RegisterAsync(request, CancellationToken.None), null); }
            catch (AuthException ex) { return (null, ex); }
        }
        var results = await Task.WhenAll(Register(first), Register(second));
        var winner = Assert.Single(results, r => r.Account is not null).Account!;
        Assert.Equal(409, Assert.Single(results, r => r.Error is not null).Error!.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        Assert.Equal(1, await db.MemberProfiles.CountAsync(x => x.UserId == winner.Id));
        Assert.Equal(1, await db.VerificationEmailDeliveries.CountAsync(x => x.UserId == winner.Id));
    }

    [Fact]
    public async Task ProviderFailure_IsRetriedWithoutAnotherRegistration()
    {
        var request = Request();
        var account = await RegisterAsync(request);
        var sender = new TestEmail { FailingAddress = request.Email };
        await DrainAsync(sender);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
            var delivery = await db.VerificationEmailDeliveries.SingleAsync(x => x.UserId == account.Id);
            Assert.Equal(1, delivery.Attempts);
            Assert.Null(delivery.LeaseId);
            Assert.True(delivery.NextAttemptAtUtc > DateTimeOffset.UtcNow);
            Assert.True(await db.Users.AnyAsync(x => x.Id == account.Id));
            await db.VerificationEmailDeliveries.Where(x => x.UserId == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        sender.FailingAddress = null;
        await DrainAsync(sender);
        Assert.Equal(2, sender.Attempts.Count(x => x == request.Email));
        using var check = factory.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<AyneraDbContext>()
            .VerificationEmailDeliveries.AnyAsync(x => x.UserId == account.Id));
    }

    [Fact]
    public async Task ConcurrentWorkers_ClaimEachDeliveryOnce()
    {
        var request = Request();
        await RegisterAsync(request);
        var sender = new TestEmail();
        await Task.WhenAll(DrainAsync(sender), DrainAsync(sender));
        Assert.Equal(1, sender.Attempts.Count(x => x == request.Email));
    }

    [Fact]
    public async Task ExpiredLease_IsRecoveredAfterWorkerCrash()
    {
        var request = Request();
        var account = await RegisterAsync(request);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
            await db.VerificationEmailDeliveries.Where(x => x.UserId == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseId, Guid.NewGuid())
                    .SetProperty(x => x.LeaseUntilUtc, DateTimeOffset.UtcNow.AddMinutes(1)));
        }
        var sender = new TestEmail();
        await DrainAsync(sender);
        Assert.DoesNotContain(request.Email, sender.Attempts);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().VerificationEmailDeliveries
                .Where(x => x.UserId == account.Id).ExecuteUpdateAsync(s =>
                    s.SetProperty(x => x.LeaseUntilUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        await DrainAsync(sender);
        Assert.Equal(1, sender.Attempts.Count(x => x == request.Email));
    }

    private async Task<AuthAccountDto> RegisterAsync(CreateMemberRequest request)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRegistrationService>().RegisterAsync(request, CancellationToken.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/verify")]
    public async Task InvalidDeliveryConfiguration_RejectsBeforeCreatingAccount(string url)
    {
        var request = Request();
        using var scope = factory.Services.CreateScope();
        var service = ActivatorUtilities.CreateInstance<RegistrationService>(scope.ServiceProvider,
            Options.Create(new EmailOptions { VerifyLinkBaseUrl = url }));
        var error = await Assert.ThrowsAsync<AuthException>(() => service.RegisterAsync(request, CancellationToken.None));
        Assert.Equal("email_config_invalid", error.ErrorCode);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().Users
            .IgnoreQueryFilters().AnyAsync(x => x.Email == request.Email));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletedOrConfirmedAccount_DiscardsObsoleteDelivery(bool deleted)
    {
        var request = Request();
        var account = await RegisterAsync(request);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == account.Id);
            user.IsDeleted = deleted;
            user.EmailConfirmed = !deleted;
            await db.SaveChangesAsync();
        }
        var sender = new TestEmail();
        await DrainAsync(sender);
        Assert.DoesNotContain(request.Email, sender.Attempts);
        using var check = factory.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<AyneraDbContext>()
            .VerificationEmailDeliveries.AnyAsync(x => x.UserId == account.Id));
    }

    private async Task DrainAsync(IEmailService sender)
    {
        for (var batch = 0; batch < 5; batch++)
        {
            using var scope = factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            await new VerificationEmailDispatcher(sp.GetRequiredService<AyneraDbContext>(),
                sp.GetRequiredService<IUserRepository>(), sender, sp.GetRequiredService<IOptions<EmailOptions>>(),
                sp.GetRequiredService<ILogger<VerificationEmailDispatcher>>()).DispatchPendingAsync(CancellationToken.None);
        }
    }

    public class IdentityWriteObserver : DispatchProxy
    {
        public IUserRepository Inner { get; set; } = null!;
        public Guid CreatedId { get; private set; }
        public bool Fail { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Inner, args);
            return method.Name == nameof(IUserRepository.CreateMemberAsync) ? ObserveAsync((Task<UserRecord>)result!) : result;
        }
        private async Task<UserRecord> ObserveAsync(Task<UserRecord> result)
        {
            var user = await result;
            CreatedId = user.Id;
            if (Fail) throw new InvalidOperationException("Injected identity failure.");
            return user;
        }
    }

    private sealed class FailingProfile(IMemberProfileRepository inner) : IMemberProfileRepository
    {
        public async Task<MemberProfileRecord> CreateAsync(MemberProfileRecord profile, CancellationToken cancellationToken)
        { await inner.CreateAsync(profile, cancellationToken); throw new InvalidOperationException("Injected profile failure."); }
        public Task<MemberProfileRecord?> FindByUserIdAsync(Guid id, CancellationToken ct) => inner.FindByUserIdAsync(id, ct);
        public Task SoftDeleteByUserIdAsync(Guid id, CancellationToken ct) => inner.SoftDeleteByUserIdAsync(id, ct);
    }

    private sealed class FailingQueue(IVerificationEmailQueue inner, CancellationTokenSource? cancellation) : IVerificationEmailQueue
    {
        public Task CancelAsync(Guid id, CancellationToken ct) => inner.CancelAsync(id, ct);
        public async Task EnqueueAsync(Guid id, CancellationToken ct)
        {
            await inner.EnqueueAsync(id, ct);
            if (cancellation is not null) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("Injected queue failure.");
        }
    }

    private sealed class TestEmail : IEmailService
    {
        public string? FailingAddress { get; set; }
        public ConcurrentBag<string> Attempts { get; } = [];
        public async Task SendVerificationLinkAsync(string address, string url, CancellationToken ct)
        {
            Attempts.Add(address);
            await Task.Delay(20, ct);
            if (address == FailingAddress) throw new InvalidOperationException("Injected provider failure.");
        }
        public Task SendOtpAsync(string address, string code, CancellationToken ct) => throw new NotSupportedException();
        public Task SendVenueHeadsUpAsync(
            string address,
            Aynera.Application.Features.Venues.Models.VenueHeadsUpNotice notice,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
