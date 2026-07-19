using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class GetBalanceHistoryQueryHandler(
    IBalanceSnapshotRepository balanceSnapshots,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<BalanceSnapshotResult>> HandleAsync(
        GetBalanceHistoryQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot list any sub-account's balance history.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var listed = await balanceSnapshots.ListBySubAccountAsync(
            query.SubAccountId, callerContext.CallerId, cancellationToken);

        return listed.Select(BalanceSnapshotResult.Map).ToList();
    }
}
