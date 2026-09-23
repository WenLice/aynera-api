using System.Reflection;
using Aynera.Application.Common;
using Aynera.Application.Features.Audit.Services.Interfaces;
using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Microsoft.Extensions.Options;
using Aynera.Application.Features.Photos.Repositories;
using Aynera.Application.Features.Preferences.Repositories;
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
using Aynera.Application.Features.Media.Storage;
using Aynera.Domain.Media.Enums;
using Aynera.Domain.Media.Statics;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class AccountDeletionConsistencyTests(AuthApiFactory factory)
{
    [Theory]
    [InlineData("sessions")]
    [InlineData("profile")]
    [InlineData("photos")]
    [InlineData("video")]
    [InlineData("email")]
    [InlineData("account")]
    [InlineData("cancellation")]
    public async Task FailedDeletion_RollsBackAllStores_AndRetryCompletes(string failure)
    {
        var (id, request) = await CreateMemberWithDataAsync();
        var (otherId, _) = await CreateMemberWithDataAsync();
        var audit = new CapturingAudit();
        using (var scope = factory.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource())
        {
            var sp = scope.ServiceProvider;
            T Store<T>(string stage, string method) where T : class
            {
                var inner = sp.GetRequiredService<T>();
                if (failure != stage && !(failure == "cancellation" && stage == "account")) return inner;
                var proxy = DispatchProxy.Create<T, FailureAfterWrite>();
                var settings = (FailureAfterWrite)(object)proxy;
                settings.Inner = inner;
                settings.Method = method;
                settings.Cancellation = failure == "cancellation" ? cancellation : null;
                return proxy;
            }
            var service = new AccountLifecycleService(
                Store<IUserRepository>("account", nameof(IUserRepository.SoftDeleteMemberAsync)),
                Store<IMemberProfileRepository>("profile", nameof(IMemberProfileRepository.SoftDeleteByUserIdAsync)),
                sp.GetRequiredService<IMemberPreferencesRepository>(),
                Store<IMemberPhotoRepository>("photos", nameof(IMemberPhotoRepository.SoftDeleteAllForUserAsync)),
                Store<IIntroductionVideoRepository>("video", nameof(IIntroductionVideoRepository.SoftDeleteByUserIdAsync)),
                Store<IRefreshSessionRepository>("sessions", nameof(IRefreshSessionRepository.SoftDeleteAllForUserAsync)),
                audit, sp.GetRequiredService<ILogger<AccountLifecycleService>>(), sp.GetRequiredService<IWorkflowTransaction>(),
                Store<IVerificationEmailQueue>("email", nameof(IVerificationEmailQueue.CancelAsync)),
                sp.GetRequiredService<IOtpChallengeRepository>(), sp.GetRequiredService<ISmsService>(),
                sp.GetRequiredService<IEmailService>(),
                sp.GetRequiredService<IOptions<OtpOptions>>(),
                sp.GetRequiredService<IOptions<JwtOptions>>());
            if (failure == "cancellation")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteMemberAsync(id, cancellation.Token));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteMemberAsync(id, cancellation.Token));
        }
        Assert.Empty(audit.Events);
        await AssertStateAsync(id, deleted: false);
        await AssertStateAsync(otherId, deleted: false);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>().DeleteMemberAsync(id, CancellationToken.None);
        }
        await AssertStateAsync(id, deleted: true);
        await AssertStateAsync(otherId, deleted: false);
        using (var scope = factory.Services.CreateScope())
        {
            var replacement = await scope.ServiceProvider.GetRequiredService<IRegistrationService>().RegisterAsync(request, CancellationToken.None);
            Assert.NotEqual(id, replacement.Id);
        }
    }

    private async Task<(Guid Id, CreateMemberRequest Request)> CreateMemberWithDataAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var request = new CreateMemberRequest("9" + Random.Shared.NextInt64(100_000_000, 999_999_999),
            "Ada Lovelace", Gender.Female, new DateOnly(1990, 5, 15), "Bangalore",
            $"deletion-{Guid.NewGuid():N}@example.com", Hometown: "Pune", Password: "secret12");
        var account = await sp.GetRequiredService<IRegistrationService>().RegisterAsync(request, CancellationToken.None);
        var db = sp.GetRequiredService<AyneraDbContext>();
        var storage = sp.GetRequiredService<IMediaStorage>();
        var photoKey = MediaKeys.Photo(account.Id, 1);
        var videoKey = MediaKeys.IntroVideo(account.Id, "video/mp4");
        await storage.PutAsync(photoKey, [1, 2, 3], "image/jpeg", CancellationToken.None);
        await storage.PutAsync(videoKey, [4, 5, 6], "video/mp4", CancellationToken.None);
        db.MemberMedia.Add(new MemberMedia { Id = Guid.NewGuid(), UserId = account.Id, Kind = MediaKind.Photo, Index = 1, StorageKey = photoKey, ContentType = "image/jpeg", ByteSize = 3, IsReference = true });
        db.MemberMedia.Add(new MemberMedia { Id = Guid.NewGuid(), UserId = account.Id, Kind = MediaKind.IntroVideo, StorageKey = videoKey, ContentType = "video/mp4", ByteSize = 3 });
        db.RefreshSessions.Add(new RefreshSession { Id = Guid.NewGuid(), UserId = account.Id, Audience = "member", TokenHash = Guid.NewGuid().ToString("N"), FamilyId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        return (account.Id, request);
    }

    private async Task AssertStateAsync(Guid id, bool deleted)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        Assert.Equal(deleted, user.IsDeleted);
        Assert.Equal(!deleted, user.IsActive);
        Assert.Equal(deleted, (await db.MemberProfiles.IgnoreQueryFilters().SingleAsync(x => x.UserId == id)).IsDeleted);
        var photo = await db.MemberMedia.IgnoreQueryFilters().SingleAsync(x => x.UserId == id && x.Kind == MediaKind.Photo);
        Assert.Equal(deleted, photo.IsDeleted);
        // Current retention remains soft deletion: the file stays in the bucket either way, because
        // account deletion runs in a database transaction that object storage cannot join.
        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
        Assert.Equal(new byte[] { 1, 2, 3 }, await storage.GetAsync(photo.StorageKey, CancellationToken.None));
        Assert.Equal(deleted, (await db.MemberMedia.IgnoreQueryFilters().SingleAsync(x => x.UserId == id && x.Kind == MediaKind.IntroVideo)).IsDeleted);
        var session = await db.RefreshSessions.IgnoreQueryFilters().SingleAsync(x => x.UserId == id);
        Assert.Equal(deleted, session.IsDeleted);
        Assert.Equal(deleted, session.RevokedAtUtc.HasValue);
        Assert.Equal(!deleted, await db.VerificationEmailDeliveries.AnyAsync(x => x.UserId == id));
        Assert.Equal(deleted ? 1 : 0, await db.AuditEvents.CountAsync(x => x.SubjectUserId == id && x.Action == AuditActions.MemberDeleted));
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
            throw new InvalidOperationException("Injected deletion failure after persistence.");
        }
    }

    private sealed class CapturingAudit : IAuditWriter
    {
        public List<AuditEventWriteModel> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteModel model, CancellationToken ct)
        { Events.Add(model); return Task.CompletedTask; }
    }
}
