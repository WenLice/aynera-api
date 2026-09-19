using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure;

public sealed class EarlyAccessCitySeedHostedService : IHostedService
{
    /// <summary>
    /// Launch catalog defaults for a fresh database. The pilot is Bangalore only.
    ///
    /// Delhi and Mumbai were seeded as Wave 2 so the public site could collect interest; they
    /// were withdrawn on 2026-09-20 (migration <c>WithdrawWaveTwoCities</c>) because offering a
    /// city the product cannot yet serve collects signups nobody will honour. Add later waves
    /// back through <c>POST /early-access/cities/Create</c> when that city actually opens —
    /// this seeder only fills a fresh database and never touches existing rows.
    /// </summary>
    private static readonly (string Name, int Wave, int SortOrder)[] SeedCities =
    [
        ("Bangalore", 1, 1)
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EarlyAccessCitySeedHostedService> _logger;

    public EarlyAccessCitySeedHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<EarlyAccessCitySeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();

        foreach (var (name, wave, sortOrder) in SeedCities)
        {
            var exists = await db.EarlyAccessCities
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Name == name, cancellationToken);
            if (exists)
            {
                continue;
            }

            db.EarlyAccessCities.Add(new EarlyAccessCity
            {
                Id = Guid.NewGuid(),
                Name = name,
                Wave = wave,
                SortOrder = sortOrder,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        var added = await db.SaveChangesAsync(cancellationToken);
        if (added > 0)
        {
            _logger.LogInformation("Seeded {Count} early-access cities", added);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
