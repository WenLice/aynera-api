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
    /// Launch catalog defaults for a fresh database. Wave 1 is open for registration;
    /// later waves are listed so the public site can collect interest. Bangalore opens first.
    /// Existing rows are never touched here — see the BangaloreOpensFirst migration, and
    /// <c>PATCH /early-access/cities/{id}</c> for changes after that.
    /// </summary>
    private static readonly (string Name, int Wave, int SortOrder)[] SeedCities =
    [
        ("Bangalore", 1, 1),
        ("Delhi", 2, 2),
        ("Mumbai", 2, 3)
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
