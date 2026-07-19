using TreasuryServiceOrchestrator.Application.Ledger;

namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record BalanceSnapshotResponse(
    Guid BalanceSnapshotId,
    Guid SubAccountId,
    Money Balance,
    BalanceSnapshotReason Reason,
    DateTime CapturedAtUtc)
{
    public static BalanceSnapshotResponse Map(BalanceSnapshotResult result) => new(
        result.BalanceSnapshotId,
        result.SubAccountId,
        result.Balance,
        result.Reason,
        result.CapturedAtUtc);
}
