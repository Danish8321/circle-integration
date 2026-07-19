using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Ledger.Snapshots;

namespace TreasuryServiceOrchestrator.Infrastructure.Snapshots;

/// <summary>
/// Polls <see cref="ScheduledBalanceSnapshotService"/> for the life of the host, at
/// <see cref="BalanceSnapshotOptions.IntervalSeconds"/> (Ticket 18.2, mirrors
/// <c>DepositReconciliationBackgroundService</c> from Ticket 15.6). Registered via
/// <c>AddHostedService&lt;ScheduledBalanceSnapshotBackgroundService&gt;()</c>. A poll-iteration
/// failure must never propagate out of <see cref="ExecuteAsync"/>: BackgroundService's default
/// host behavior stops the entire host on an unhandled exception, which the periodic-snapshot
/// job must not allow.
/// </summary>
public sealed partial class ScheduledBalanceSnapshotBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<BalanceSnapshotOptions> options,
    TimeProvider timeProvider,
    ILogger<ScheduledBalanceSnapshotBackgroundService> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled balance snapshot pass recorded {SnapshotCount} snapshot(s).")]
    private partial void LogPassCompleted(int snapshotCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled balance snapshot poll iteration failed.")]
    private partial void LogPollIterationFailed(Exception ex);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var snapshotService = scope.ServiceProvider.GetRequiredService<ScheduledBalanceSnapshotService>();
                var snapshotCount = await snapshotService.RunOnceAsync(stoppingToken);
                LogPassCompleted(snapshotCount);
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
