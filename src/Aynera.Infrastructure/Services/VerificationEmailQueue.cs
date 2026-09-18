using Microsoft.EntityFrameworkCore;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Persistence;
using Aynera.Persistence.Entities;

namespace Aynera.Infrastructure.Services;

public sealed class VerificationEmailQueue(AyneraDbContext db) : IVerificationEmailQueue
{
    public async Task CancelAsync(Guid userId, CancellationToken cancellationToken)
    {
        await db.VerificationEmailDeliveries.Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task EnqueueAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        db.VerificationEmailDeliveries.Add(new VerificationEmailDelivery
        {
            UserId = userId, CreatedAtUtc = now, NextAttemptAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
