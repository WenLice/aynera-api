using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Application.Features.Venues.Models;
using Aynera.Application.Features.Venues.Repositories;
using Aynera.Domain.Venues.Enums;
using Aynera.Domain.Venues.Validators;
using Aynera.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure.Services;

/// <summary>
/// Delivers queued venue heads-up notifications. Mirrors the verification-email outbox:
/// lease a row, resolve the recipient from the venue at send time, deliver, then mark sent.
/// Never holds a database transaction across the provider call.
/// </summary>
public sealed class VenueNotificationDispatcher(
    AyneraDbContext db,
    IVenueRepository venues,
    IEmailService email,
    ISmsService sms,
    ILogger<VenueNotificationDispatcher> logger)
{
    private const int MaxAttempts = 8;

    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.VenueNotifications.AsNoTracking()
            .Where(x => x.SentAtUtc == null && x.AbandonedAtUtc == null
                && x.NextAttemptAtUtc <= now && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
            .OrderBy(x => x.NextAttemptAtUtc).Take(25).ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseId = Guid.NewGuid();
            var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            var claimed = await db.VenueNotifications
                .Where(x => x.Id == candidate.Id && x.SentAtUtc == null && x.AbandonedAtUtc == null
                    && x.NextAttemptAtUtc <= now && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseId, leaseId)
                    .SetProperty(x => x.LeaseUntilUtc, leaseUntil), cancellationToken);
            if (claimed == 0) continue;

            var owned = db.VenueNotifications.Where(x => x.Id == candidate.Id && x.LeaseId == leaseId);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(1));

                var venue = await venues.FindByIdAsync(candidate.VenueId, timeout.Token);
                if (venue is not { IsActive: true })
                {
                    await AbandonAsync(owned, "venue_unavailable", cancellationToken);
                    continue;
                }

                if (candidate.VisitOn < VenueValidation.TodayInIndia)
                {
                    await AbandonAsync(owned, "visit_passed", cancellationToken);
                    continue;
                }

                var notice = new VenueHeadsUpNotice(
                    venue.Name, venue.Area, candidate.VisitOn, candidate.PartySize, candidate.Note);

                if (candidate.Channel == VenueNotificationChannel.Email)
                {
                    await email.SendVenueHeadsUpAsync(venue.ContactEmail, notice, timeout.Token);
                }
                else
                {
                    await sms.SendVenueHeadsUpAsync(venue.ContactPhoneE164, notice, timeout.Token);
                }

                var sentAt = DateTimeOffset.UtcNow;
                await owned.ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.SentAtUtc, sentAt)
                    .SetProperty(x => x.LeaseId, (Guid?)null)
                    .SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var attempts = candidate.Attempts + 1;
                var errorType = ex.GetType().Name;
                if (attempts >= MaxAttempts)
                {
                    await AbandonAsync(owned, errorType, cancellationToken, attempts);
                    logger.LogWarning(
                        "Venue heads-up {NotificationId} abandoned after {Attempts} attempts; error type {ErrorType}",
                        candidate.Id, attempts, errorType);
                    continue;
                }

                var next = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Min(3600, 30 * Math.Pow(2, Math.Min(candidate.Attempts, 7))));
                await owned.ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.NextAttemptAtUtc, next)
                    .SetProperty(x => x.LastError, errorType)
                    .SetProperty(x => x.LeaseId, (Guid?)null)
                    .SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null), cancellationToken);
                logger.LogWarning(
                    "Venue heads-up {NotificationId} delivery failed; attempt {Attempt}, error type {ErrorType}, next attempt {NextAttempt}",
                    candidate.Id, attempts, errorType, next);
            }
        }
    }

    private static async Task AbandonAsync(
        IQueryable<Persistence.Entities.VenueNotification> owned,
        string reason,
        CancellationToken cancellationToken, int? failedAttempts = null)
    {
        var abandonedAt = DateTimeOffset.UtcNow;
        await owned.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.AbandonedAtUtc, abandonedAt)
            .SetProperty(x => x.LastError, reason)
            .SetProperty(x => x.Attempts, x => failedAttempts ?? x.Attempts)
            .SetProperty(x => x.LeaseId, (Guid?)null)
            .SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null), cancellationToken);
    }
}
