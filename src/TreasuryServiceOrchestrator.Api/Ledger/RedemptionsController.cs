using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Shared;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/redemptions")]
public sealed class RedemptionsController(
    ListRedemptionsQueryHandler listRedemptionsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RedemptionResponse>>> ListRedemptions(
        Guid subAccountId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var pageRequest = new PageRequest(page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize);
        var results = await listRedemptionsHandler.HandleAsync(
            new ListRedemptionsQuery(subAccountId, pageRequest), cancellationToken);

        return Ok(results.Select(RedemptionResponse.Map).ToList());
    }
}
