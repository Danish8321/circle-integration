using Microsoft.Extensions.Logging;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Snapshots;

/// <summary>
/// Periodic job (ticket 18.1) that records a point-in-time <see cref="BalanceSnapshot"/> with
/// <see cref="BalanceSnapshotReason.Scheduled"/> for every tenant's current
/// <see cref="FundAccount"/> balance. Purely observational — never mutates a balance itself.
/// Mirrors <see cref="Reconciliation.DepositReconciliationService"/>'s per-item try/catch shape:
/// one account's failure must not abort the rest of the pass.
/// </summary>
public sealed partial class ScheduledBalanceSnapshotService(
    IFundAccountRepository fundAccountRepository,
    ISubAccountRepository subAccountRepository,
    IBalanceSnapshotRepository balanceSnapshotRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ScheduledBalanceSnapshotService> logger)
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Scheduled snapshot skipped: no sub-account found for fund account {FundAccountId} (client company {ClientCompanyId})")]
    private partial void LogNoSubAccountFound(Guid fundAccountId, string clientCompanyId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Scheduled balance snapshot failed for fund account {FundAccountId} (client company {ClientCompanyId})")]
    private partial void LogSnapshotFailed(Exception ex, Guid fundAccountId, string clientCompanyId);

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var fundAccounts = await fundAccountRepository.ListAllAsync(cancellationToken);

        var snapshotCount = 0;
        foreach (var fundAccount in fundAccounts)
        {
            if (await TrySnapshotAsync(fundAccount, cancellationToken))
            {
                snapshotCount++;
            }
        }

        return snapshotCount;
    }

    private async Task<bool> TrySnapshotAsync(FundAccount fundAccount, CancellationToken cancellationToken)
    {
        try
        {
            var subAccount = await subAccountRepository.GetByClientCompanyIdAsync(
                fundAccount.ClientCompanyId, cancellationToken);
            if (subAccount is null)
            {
                // No sub-account on file for this tenant's fund account — nothing to key the
                // snapshot off of (BalanceSnapshot requires a SubAccountId). Skip, don't abort
                // the rest of the pass.
                LogNoSubAccountFound(fundAccount.Id, fundAccount.ClientCompanyId);
                return false;
            }

            var snapshot = BalanceSnapshot.Create(
                subAccount.Id,
                fundAccount.ClientCompanyId,
                fundAccount.Balance,
                BalanceSnapshotReason.Scheduled,
                timeProvider.GetUtcNow().UtcDateTime);

            await balanceSnapshotRepository.AddAsync(snapshot, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failure writing one account's snapshot must not abort the rest of the pass.
            LogSnapshotFailed(ex, fundAccount.Id, fundAccount.ClientCompanyId);
            return false;
        }
    }
}
