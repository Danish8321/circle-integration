using Microsoft.Extensions.Logging;

using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Services;

/// <summary>
/// Self-healing backstop for deposits the webhook path silently missed (PRD §11.4). Reuses the
/// exact <see cref="ProcessDepositCommandHandler"/> the webhook path uses — reconciliation is not
/// a parallel crediting mechanism. See docs/features/05-reliability-and-error-handling.md §7.4.
/// </summary>
public sealed partial class DepositReconciliationService(
    ISubAccountRepository subAccountRepository,
    IStablecoinGateway stablecoinGateway,
    ITransactionRepository transactionRepository,
    ICommandHandler<ProcessDepositCommand, ProcessDepositResult> processDepositHandler,
    ISettableCallerContext callerContext,
    TimeProvider timeProvider,
    ReconciliationOptions options,
    ILogger<DepositReconciliationService> logger)
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Deposit reconciliation failed to list deposits for wallet {CircleWalletId} (sub-account {SubAccountId})")]
    private partial void LogListDepositsFailed(Exception ex, string circleWalletId, Guid subAccountId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Deposit reconciliation failed to process deposit {ProviderReferenceId} for sub-account {SubAccountId}")]
    private partial void LogProcessDepositFailed(Exception ex, string providerReferenceId, Guid subAccountId);

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var sinceUtc = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-options.LookbackWindowMinutes);
        var subAccounts = await subAccountRepository.ListActiveWithWalletAsync(cancellationToken);

        var healedCount = 0;

        foreach (var subAccount in subAccounts)
        {
            healedCount += await ReconcileWalletAsync(subAccount, sinceUtc, cancellationToken);
        }

        return healedCount;
    }

    private async Task<int> ReconcileWalletAsync(
        SubAccount subAccount, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        // CircleWalletId is guaranteed non-null by ListActiveWithWalletAsync's contract
        // (excludes walletless sub-accounts).
        var circleWalletId = subAccount.CircleWalletId!;

        IReadOnlyList<ProviderDepositRecord> deposits;
        try
        {
            deposits = await stablecoinGateway.ListRecentDepositsAsync(
                circleWalletId, sinceUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // §7.4: a gateway failure for one wallet must not abort the rest of the pass.
            LogListDepositsFailed(ex, circleWalletId, subAccount.Id);
            return 0;
        }

        var healedCount = 0;
        foreach (var deposit in deposits)
        {
            if (await TryReconcileDepositAsync(subAccount, deposit, cancellationToken))
            {
                healedCount++;
            }
        }

        return healedCount;
    }

    private async Task<bool> TryReconcileDepositAsync(
        SubAccount subAccount, ProviderDepositRecord deposit, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await transactionRepository.GetByProviderReferenceIdAsync(
                deposit.ProviderReferenceId, cancellationToken);
            if (existing is not null)
            {
                // Already recorded via the webhook path — dedup, do not double-credit.
                return false;
            }

            // The reconciliation pass has no HTTP caller, so the tenant identity that
            // ProcessDepositCommandHandler reads via ICallerContext (invariant 7) must be
            // established here — same pattern as DepositsWebhookTopicProcessor.
            callerContext.Set(subAccount.ClientCompanyId, CallerRole.SubAccount);

            var command = new ProcessDepositCommand(
                subAccount.Id,
                deposit.Amount,
                deposit.ProviderReferenceId,
                deposit.SourceType,
                $"reconciliation-{deposit.ProviderReferenceId}");

            await processDepositHandler.HandleAsync(command, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // §7.4: a per-deposit failure must not abort the rest of that wallet's deposits or
            // the rest of the pass.
            LogProcessDepositFailed(ex, deposit.ProviderReferenceId, subAccount.Id);
            return false;
        }
    }
}
