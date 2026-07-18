using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TreasuryServiceOrchestrator.Infrastructure.Notifications;

/// <summary>
/// Polls <see cref="NotificationDispatcher"/> for the life of the host, at
/// <see cref="NotificationDispatcherOptions.PollingIntervalMilliseconds"/>
/// (docs/features/13-internal-notifications-outbox.md §4.4). Registered via
/// <c>AddHostedService&lt;NotificationDispatchBackgroundService&gt;()</c>.
/// </summary>
public sealed class NotificationDispatchBackgroundService(
    NotificationDispatcher dispatcher,
    IOptions<NotificationDispatcherOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.PollingIntervalMilliseconds));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await dispatcher.DispatchDueBatchAsync(stoppingToken);
                await Task.Delay(pollInterval, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — swallow so the service stops cleanly.
        }
    }
}
