
namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class ListAllTransactionsQueryHandler(ITransactionRepository transactions)
{
    public async Task<IReadOnlyList<AdminTransactionResult>> HandleAsync(
        ListAllTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        var listed = await transactions.ListAllAsync(query.Filter, cancellationToken);
        return listed.Select(AdminTransactionResult.Map).ToList();
    }
}
