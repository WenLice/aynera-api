using System.Collections.Concurrent;
using System.Reflection;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using Aynera.Domain.Venues.Enums;
using Aynera.Domain.Venues.Validators;
using Aynera.Infrastructure.Services;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Api.IntegrationTests;

[Collection("Integration")]
public sealed class VenueDeliveryConsistencyTests(AuthApiFactory factory)
{
    [Fact]
    public async Task FailedEmail_RetriesWithoutResendingSuccessfulSms()
    {
        var id = await SeedAsync();
        var sender = new DeliveryCounter { FailEmail = true };
        await DispatchAsync(sender);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
            var email = await db.VenueNotifications.SingleAsync(x => x.VenueId == id && x.Channel == VenueNotificationChannel.Email);
            Assert.Null(email.SentAtUtc);
            Assert.Equal(1, email.Attempts);
            Assert.Equal(nameof(InvalidOperationException), email.LastError);
            Assert.Null(email.LeaseId);
            Assert.True(email.NextAttemptAtUtc > email.CreatedAtUtc);
            Assert.NotNull((await db.VenueNotifications.SingleAsync(x => x.VenueId == id && x.Channel == VenueNotificationChannel.Sms)).SentAtUtc);
            await db.VenueNotifications.Where(x => x.VenueId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        sender.FailEmail = false;
        await DispatchAsync(sender);
        await DispatchAsync(sender);
        Assert.Equal(2, sender.Count(id, "email"));
        Assert.Equal(1, sender.Count(id, "sms"));
    }

    [Fact]
    public async Task ConcurrentDispatchers_ClaimEachChannelOnce()
    {
        var id = await SeedAsync();
        var sender = new DeliveryCounter();
        await Task.WhenAll(DispatchAsync(sender), DispatchAsync(sender));
        Assert.Equal(1, sender.Count(id, "email"));
        Assert.Equal(1, sender.Count(id, "sms"));
    }

    [Fact]
    public async Task ExpiredLease_IsRecovered_ButLiveLeaseIsRespected()
    {
        var id = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().VenueNotifications.Where(x => x.VenueId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseId, Guid.NewGuid())
                    .SetProperty(x => x.LeaseUntilUtc, DateTimeOffset.UtcNow.AddMinutes(5)));
        var sender = new DeliveryCounter();
        await DispatchAsync(sender);
        Assert.Equal(0, sender.Count(id, "email"));
        Assert.Equal(0, sender.Count(id, "sms"));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().VenueNotifications.Where(x => x.VenueId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntilUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await DispatchAsync(sender);
        Assert.Equal(1, sender.Count(id, "email"));
        Assert.Equal(1, sender.Count(id, "sms"));
    }

    [Theory]
    [InlineData("inactive", "venue_unavailable")]
    [InlineData("deleted", "venue_unavailable")]
    [InlineData("past", "visit_passed")]
    public async Task ObsoleteNotice_IsAbandonedWithoutSending(string state, string reason)
    {
        var id = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
            if (state == "inactive") await db.Venues.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            else if (state == "deleted") await db.Venues.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            else await db.VenueNotifications.Where(x => x.VenueId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.VisitOn, VenueValidation.TodayInIndia.AddDays(-1)));
        }
        var sender = new DeliveryCounter();
        await DispatchAsync(sender);
        Assert.Equal(0, sender.Count(id, "email"));
        Assert.Equal(0, sender.Count(id, "sms"));
        using var check = factory.Services.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<AyneraDbContext>().VenueNotifications.Where(x => x.VenueId == id).ToListAsync();
        Assert.All(rows, row => { Assert.NotNull(row.AbandonedAtUtc); Assert.Equal(reason, row.LastError); Assert.Null(row.LeaseId); });
    }

    [Fact]
    public async Task LastFailure_RecordsEighthAttempt_AndStopsRetrying()
    {
        var id = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AyneraDbContext>().VenueNotifications.Where(x => x.VenueId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, 7));
        var sender = new DeliveryCounter { FailEmail = true };
        await DispatchAsync(sender);
        await DispatchAsync(sender);
        Assert.Equal(1, sender.Count(id, "email"));
        using var check = factory.Services.CreateScope();
        var row = await check.ServiceProvider.GetRequiredService<AyneraDbContext>().VenueNotifications
            .SingleAsync(x => x.VenueId == id && x.Channel == VenueNotificationChannel.Email);
        Assert.Equal(8, row.Attempts);
        Assert.NotNull(row.AbandonedAtUtc);
        Assert.Null(row.SentAtUtc);
    }

    private async Task<Guid> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        var id = Guid.NewGuid();
        db.Venues.Add(new Venue { Id = id, Name = id.ToString(), Type = VenueType.Cafe,
            CityId = Guid.NewGuid(), Area = "Delhi", Address = "Test street", ContactName = "Test contact",
            ContactEmail = $"{id:N}@example.com", ContactPhoneE164 = "+919876543210", IsActive = true });
        foreach (var channel in new[] { VenueNotificationChannel.Email, VenueNotificationChannel.Sms })
            db.VenueNotifications.Add(new VenueNotification { Id = Guid.NewGuid(), VenueId = id, Channel = channel,
                VisitOn = VenueValidation.TodayInIndia.AddDays(2), PartySize = 3, CreatedByUserId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow, NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task DispatchAsync(DeliveryCounter counter)
    {
        using var scope = factory.Services.CreateScope();
        T Sender<T>(string channel) where T : class
        {
            var proxy = DispatchProxy.Create<T, NoticeSender>();
            var settings = (NoticeSender)(object)proxy;
            settings.Counter = counter;
            settings.Channel = channel;
            return proxy;
        }
        var dispatcher = ActivatorUtilities.CreateInstance<VenueNotificationDispatcher>(scope.ServiceProvider,
            Sender<IEmailService>("email"), Sender<ISmsService>("sms"));
        await dispatcher.DispatchPendingAsync(CancellationToken.None);
    }

    public class NoticeSender : DispatchProxy
    {
        public DeliveryCounter Counter { get; set; } = null!;
        public string Channel { get; set; } = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var notice = (VenueHeadsUpNotice)args![1]!;
            Counter.Counts.AddOrUpdate($"{notice.VenueName}:{Channel}", 1, (_, count) => count + 1);
            return Channel == "email" && Counter.FailEmail
                ? Task.FromException(new InvalidOperationException("Simulated provider error containing private details"))
                : Task.CompletedTask;
        }
    }

    public sealed class DeliveryCounter
    {
        public ConcurrentDictionary<string, int> Counts { get; } = new();
        public bool FailEmail { get; set; }
        public int Count(Guid id, string channel) => Counts.GetValueOrDefault($"{id}:{channel}");
    }
}
