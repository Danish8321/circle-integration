using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/transactions/{transactionId:guid}")]
public sealed class TransactionDetailController(
    GetTransactionQueryHandler getTransactionHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TransactionResponse>> GetTransaction(
        Guid subAccountId, Guid transactionId, CancellationToken cancellationToken)
    {
        var result = await getTransactionHandler.HandleAsync(
            new GetTransactionQuery(transactionId), cancellationToken);

        return Ok(TransactionResponse.Map(result));
    }
}
