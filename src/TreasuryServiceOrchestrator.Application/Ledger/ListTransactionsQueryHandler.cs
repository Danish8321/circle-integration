using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class ListTransactionsQueryHandler(
    ITransactionRepository transactions,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<TransactionResult>> HandleAsync(
        ListTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot list any sub-account's transactions.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var listed = await transactions.ListBySubAccountAsync(
            query.SubAccountId, callerContext.CallerId, cancellationToken);

        return listed.Select(TransactionResult.Map).ToList();
    }
}
