using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class GetTransactionQueryHandler(
    ITransactionRepository transactions,
    ICallerContext callerContext)
{
    public async Task<TransactionResult> HandleAsync(
        GetTransactionQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot look up any transaction.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var transaction = await transactions.GetByIdAsync(
            query.TransactionId, callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No transaction '{query.TransactionId}'.");

        return TransactionResult.Map(transaction);
    }
}
