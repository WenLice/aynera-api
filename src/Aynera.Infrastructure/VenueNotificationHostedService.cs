using Aynera.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure;

public sealed class VenueNotificationHostedService(
    IServiceScopeFactory scopes,
    ILogger<VenueNotificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<VenueNotificationDispatcher>()
                    .DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Venue notification processing failed: {ErrorType}", ex.GetType().Name);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
