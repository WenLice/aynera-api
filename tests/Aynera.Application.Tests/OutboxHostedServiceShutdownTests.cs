using Aynera.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aynera.Application.Tests;

/// <summary>
/// The outbox workers sit in <c>PeriodicTimer.WaitForNextTickAsync(stoppingToken)</c> for nearly all of their
/// life, so a host shutdown almost always lands there. That wait used to sit outside the loop's try/catch, so
/// stopping the host faulted <c>ExecuteAsync</c> with an <see cref="OperationCanceledException"/> instead of
/// letting it finish. The host swallows that particular fault during a graceful stop, so nothing broke at
/// runtime — but it broke into the debugger on every shutdown and made the loop's own cancellation handler
/// dead code for the case it was written to cover.
/// </summary>
public sealed class OutboxHostedServiceShutdownTests
{
    // An empty provider: resolving the dispatcher throws, the loop's general catch logs it, and the service
    // proceeds to the timer wait — which is the state this test needs it to be in when the stop arrives.
    private static IServiceScopeFactory EmptyScopes() =>
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    public static TheoryData<Func<BackgroundService>> Workers() => new()
    {
        () => new VenueNotificationHostedService(
            EmptyScopes(),
            DiscardLogger<VenueNotificationHostedService>.Instance),
        () => new VerificationEmailHostedService(
            EmptyScopes(),
            DiscardLogger<VerificationEmailHostedService>.Instance),
    };

    [Theory]
    [MemberData(nameof(Workers))]
    public async Task StoppingTheWorker_CompletesExecuteTask_RatherThanCancellingIt(Func<BackgroundService> create)
    {
        using var worker = create();

        await worker.StartAsync(CancellationToken.None);

        // Let the first iteration fall through to the timer wait, where a real shutdown lands.
        await Task.Delay(150);

        await worker.StopAsync(CancellationToken.None);

        var executed = worker.ExecuteTask;
        Assert.NotNull(executed);
        Assert.True(
            executed!.IsCompleted,
            "ExecuteAsync did not finish after StopAsync.");
        Assert.False(
            executed.IsCanceled,
            "ExecuteAsync faulted with OperationCanceledException on shutdown; the timer wait is unguarded again.");
        Assert.False(
            executed.IsFaulted,
            $"ExecuteAsync faulted on shutdown: {executed.Exception?.GetBaseException().GetType().Name}.");
    }
}
