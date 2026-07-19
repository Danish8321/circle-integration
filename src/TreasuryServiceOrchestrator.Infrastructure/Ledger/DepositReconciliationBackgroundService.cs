using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

/// <summary>
/// Polls <see cref="DepositReconciliationService"/> for the life of the host, at
/// <see cref="ReconciliationOptions.IntervalSeconds"/> (Ticket 15.6,
/// docs/features/05-reliability-and-error-handling.md §7.4/§7.6). Registered via
/// <c>AddHostedService&lt;DepositReconciliationBackgroundService&gt;()</c>. A poll-iteration
/// failure must never propagate out of <see cref="ExecuteAsync"/>: BackgroundService's default
/// host behavior stops the entire host on an unhandled exception, which the self-healing
/// philosophy behind reconciliation (§7.4) explicitly must not allow.
/// </summary>
public sealed partial class DepositReconciliationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReconciliationOptions> options,
    TimeProvider timeProvider,
    ILogger<DepositReconciliationBackgroundService> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Deposit reconciliation pass healed {HealedCount} deposit(s).")]
    private partial void LogPassCompleted(int healedCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deposit reconciliation poll iteration failed.")]
    private partial void LogPollIterationFailed(Exception ex);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reconciliationService = scope.ServiceProvider.GetRequiredService<DepositReconciliationService>();
                var healedCount = await reconciliationService.RunOnceAsync(stoppingToken);
                LogPassCompleted(healedCount);
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
