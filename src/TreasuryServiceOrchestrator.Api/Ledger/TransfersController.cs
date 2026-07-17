using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/transfers")]
public sealed class TransfersController(
    ListTransfersQueryHandler listTransfersHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TransferResponse>>> ListTransfers(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var results = await listTransfersHandler.HandleAsync(
            new ListTransfersQuery(subAccountId), cancellationToken);

        return Ok(results.Select(TransferResponse.Map).ToList());
    }
}
