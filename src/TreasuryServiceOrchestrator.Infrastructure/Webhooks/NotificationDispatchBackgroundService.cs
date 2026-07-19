using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>
/// Polls <see cref="NotificationDispatcher"/> for the life of the host, at
/// <see cref="NotificationDispatcherOptions.PollingIntervalMilliseconds"/>
/// (docs/features/13-internal-notifications-outbox.md §4.4). Registered via
/// <c>AddHostedService&lt;NotificationDispatchBackgroundService&gt;()</c>. A poll-iteration
/// failure (e.g. a transient DB/scope error) must never propagate out of <see cref="ExecuteAsync"/>:
/// BackgroundService's default host behavior stops the entire host on an unhandled exception, which
/// took down WebApplicationFactory-based test hosts mid-suite before this was caught per-iteration.
/// </summary>
public sealed partial class NotificationDispatchBackgroundService(
    NotificationDispatcher dispatcher,
    IOptions<NotificationDispatcherOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatchBackgroundService> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Notification dispatch poll iteration failed.")]
    private partial void LogPollIterationFailed(Exception ex);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.PollingIntervalMilliseconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.DispatchDueBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogPollIterationFailed(ex);
            }

            try
            {
                await Task.Delay(pollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
