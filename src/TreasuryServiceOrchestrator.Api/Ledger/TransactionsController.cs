using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/transactions")]
public sealed class TransactionsController(
    ListTransactionsQueryHandler listTransactionsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TransactionResponse>>> ListTransactions(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var results = await listTransactionsHandler.HandleAsync(
            new ListTransactionsQuery(subAccountId), cancellationToken);

        return Ok(results.Select(TransactionResponse.Map).ToList());
    }
}
