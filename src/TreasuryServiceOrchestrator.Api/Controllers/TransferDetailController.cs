using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/transfers/{transferId:guid}")]
public sealed class TransferDetailController(
    GetTransferQueryHandler getTransferHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TransferResponse>> GetTransfer(
        Guid subAccountId, Guid transferId, CancellationToken cancellationToken)
    {
        var result = await getTransferHandler.HandleAsync(
            new GetTransferQuery(transferId), cancellationToken);

        return Ok(TransferResponse.Map(result));
    }
}
