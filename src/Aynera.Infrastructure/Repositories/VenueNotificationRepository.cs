using Aynera.Application.Features.Venues.Repositories;
using Aynera.Domain.Venues.Records;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aynera.Infrastructure.Repositories;

public sealed class VenueNotificationRepository : IVenueNotificationRepository
{
    private readonly AyneraDbContext _db;

    public VenueNotificationRepository(AyneraDbContext db)
    {
        _db = db;
    }

    public async Task AddRangeAsync(
        IEnumerable<VenueNotificationRecord> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var record in notifications)
        {
            _db.VenueNotifications.Add(new VenueNotification
            {
                Id = record.Id,
                VenueId = record.VenueId,
                Channel = record.Channel,
                VisitOn = record.VisitOn,
                PartySize = record.PartySize,
                Note = record.Note,
                CreatedByUserId = record.CreatedByUserId,
                CreatedAtUtc = record.CreatedAtUtc,
                NextAttemptAtUtc = record.CreatedAtUtc,
                Attempts = record.Attempts,
                SentAtUtc = record.SentAtUtc,
                AbandonedAtUtc = record.AbandonedAtUtc
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VenueNotificationRecord>> ListByVenueAsync(
        Guid venueId,
        CancellationToken cancellationToken)
    {
        var entities = await _db.VenueNotifications
            .AsNoTracking()
            .Where(x => x.VenueId == venueId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    private static VenueNotificationRecord Map(VenueNotification e) =>
        new(
            e.Id,
            e.VenueId,
            e.Channel,
            e.VisitOn,
            e.PartySize,
            e.Note,
            e.CreatedByUserId,
            e.CreatedAtUtc,
            e.Attempts,
            e.SentAtUtc,
            e.AbandonedAtUtc);
}
