using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class ListAllTransactionsQueryHandler(ITransactionRepository transactions)
{
    public async Task<IReadOnlyList<Transaction>> HandleAsync(
        ListAllTransactionsQuery query, CancellationToken cancellationToken = default) =>
        await transactions.ListAllAsync(query.Filter, cancellationToken);
}
