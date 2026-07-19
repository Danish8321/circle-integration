using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/transfers")]
public sealed class TransfersController(
    ListTransfersQueryHandler listTransfersHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TransferResponse>>> ListTransfers(
        Guid subAccountId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var pageRequest = new PageRequest(page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize);
        var results = await listTransfersHandler.HandleAsync(
            new ListTransfersQuery(subAccountId, pageRequest), cancellationToken);

        return Ok(results.Select(TransferResponse.Map).ToList());
    }
}
