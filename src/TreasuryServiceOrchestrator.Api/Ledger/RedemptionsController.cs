using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/redemptions")]
public sealed class RedemptionsController(
    ListRedemptionsQueryHandler listRedemptionsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RedemptionResponse>>> ListRedemptions(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var results = await listRedemptionsHandler.HandleAsync(
            new ListRedemptionsQuery(subAccountId), cancellationToken);

        return Ok(results.Select(RedemptionResponse.Map).ToList());
    }
}
