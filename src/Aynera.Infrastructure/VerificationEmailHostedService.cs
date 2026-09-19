using Aynera.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure;

public sealed class VerificationEmailHostedService(IServiceScopeFactory scopes,
    ILogger<VerificationEmailHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<VerificationEmailDispatcher>().DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Verification queue processing failed: {ErrorType}", ex.GetType().Name);
            }
            // The service spends nearly all its time here, so this is where shutdown almost always lands.
            // Cancellation during the wait is an ordinary stop, not a fault: swallow it so ExecuteAsync
            // completes instead of surfacing OperationCanceledException to the host.
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
