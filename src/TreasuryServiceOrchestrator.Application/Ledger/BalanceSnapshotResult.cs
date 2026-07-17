using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record BalanceSnapshotResult(
    Guid BalanceSnapshotId,
    Guid SubAccountId,
    Money Balance,
    BalanceSnapshotReason Reason,
    DateTime CapturedAtUtc)
{
    public static BalanceSnapshotResult Map(BalanceSnapshot balanceSnapshot) => new(
        balanceSnapshot.Id,
        balanceSnapshot.SubAccountId,
        balanceSnapshot.Balance,
        balanceSnapshot.Reason,
        balanceSnapshot.CapturedAtUtc);
}
