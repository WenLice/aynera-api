using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Infrastructure.Services;

public sealed class VerificationEmailDispatcher(
    AyneraDbContext db, IUserRepository users, IEmailService email,
    IOptions<EmailOptions> options, ILogger<VerificationEmailDispatcher> logger)
{
    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.VerificationEmailDeliveries.AsNoTracking()
            .Where(x => x.NextAttemptAtUtc <= now && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
            .OrderBy(x => x.NextAttemptAtUtc).Take(25).ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseId = Guid.NewGuid();
            var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            var claimed = await db.VerificationEmailDeliveries
                .Where(x => x.UserId == candidate.UserId && x.NextAttemptAtUtc <= now
                    && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseId, leaseId)
                    .SetProperty(x => x.LeaseUntilUtc, leaseUntil), cancellationToken);
            if (claimed == 0) continue;

            var owned = db.VerificationEmailDeliveries.Where(x => x.UserId == candidate.UserId && x.LeaseId == leaseId);
            try
            {
                // No database transaction is held during the provider call. A crash after send may resend.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(1));
                var user = await users.FindByIdAsync(candidate.UserId, timeout.Token);
                if (user is { IsDeleted: false, IsActive: true, IsRestricted: false, EmailConfirmed: false }
                    && !string.IsNullOrWhiteSpace(user.Email))
                {
                    var token = await users.GenerateEmailConfirmationTokenAsync(user.Id, timeout.Token);
                    var baseUrl = options.Value.VerifyLinkBaseUrl.Trim();
                    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                        throw new InvalidOperationException("Verification link configuration is invalid.");
                    var builder = new UriBuilder(uri) { Fragment = "" };
                    var query = builder.Query.TrimStart('?');
                    builder.Query = (query.Length == 0 ? "" : query + "&")
                        + $"userId={user.Id:D}&token={Uri.EscapeDataString(token)}";
                    await email.SendVerificationLinkAsync(user.Email, builder.Uri.AbsoluteUri, timeout.Token);
                }
                await owned.ExecuteDeleteAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The lease expires after shutdown/crash, allowing a different worker to retry.
                throw;
            }
            catch (Exception ex)
            {
                var next = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(candidate.Attempts, 7))));
                await owned.ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.NextAttemptAtUtc, next)
                    .SetProperty(x => x.LeaseId, (Guid?)null)
                    .SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null), cancellationToken);
                // Do not log provider messages that could contain a recipient or verification link.
                logger.LogWarning("Verification delivery failed for user {UserId}; attempt {Attempt}, error type {ErrorType}, next attempt {NextAttempt}",
                    candidate.UserId, candidate.Attempts + 1, ex.GetType().Name, next);
            }
        }
    }
}
