using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/redemptions/{redemptionId:guid}")]
public sealed class RedemptionDetailController(
    GetRedemptionQueryHandler getRedemptionHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RedemptionResponse>> GetRedemption(
        Guid subAccountId, Guid redemptionId, CancellationToken cancellationToken)
    {
        var result = await getRedemptionHandler.HandleAsync(
            new GetRedemptionQuery(redemptionId), cancellationToken);

        return Ok(RedemptionResponse.Map(result));
    }
}
