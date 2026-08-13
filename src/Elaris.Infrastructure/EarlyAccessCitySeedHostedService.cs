using Elaris.Persistence;
using Elaris.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elaris.Infrastructure;

public sealed class EarlyAccessCitySeedHostedService : IHostedService
{
    private static readonly (string Name, int Wave, int SortOrder)[] Wave1 =
    [
        ("Delhi", 1, 1),
        ("Bangalore", 1, 2),
        ("Mumbai", 1, 3)
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
        var db = scope.ServiceProvider.GetRequiredService<ElarisDbContext>();

        foreach (var (name, wave, sortOrder) in Wave1)
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
