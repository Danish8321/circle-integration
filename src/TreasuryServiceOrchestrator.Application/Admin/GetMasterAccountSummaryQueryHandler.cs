using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Application.Admin;

/// <summary>
/// Cross-module composition: main wallet balance (Ledger's IStablecoinGateway) + sum of every
/// sub-account's latest BalanceSnapshot (Ledger's IBalanceSnapshotRepository), keyed off the
/// full sub-account list (Compliance's ISubAccountRepository). No single one of these ports owns
/// this composition, which is why it lives in the Admin module rather than as a sub-namespace of
/// either (docs/features/12-admin-cross-tenant-views.md §2.4). No ICallerContext dependency here
/// — the Admin gate lives entirely in MasterAccountController.
/// </summary>
public sealed class GetMasterAccountSummaryQueryHandler(
    ISubAccountRepository subAccounts,
    IBalanceSnapshotRepository snapshots,
    IStablecoinGateway gateway)
{
    public async Task<GetMasterAccountSummaryResult> HandleAsync(
        GetMasterAccountSummaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var mainWalletBalance = await gateway.GetMainWalletBalanceAsync(cancellationToken);
        var all = await subAccounts.ListAsync(lifecycleState: null, cancellationToken);

        var total = 0m;
        foreach (var subAccount in all)
        {
            var latest = await snapshots.GetLatestAsync(
                subAccount.Id, subAccount.ClientCompanyId, cancellationToken);
            total += latest?.Balance.Amount ?? 0m;
        }

        return new GetMasterAccountSummaryResult(mainWalletBalance, new Money(total, "USDC"), all.Count);
    }
}
