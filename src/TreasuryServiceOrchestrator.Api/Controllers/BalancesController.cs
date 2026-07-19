using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/balances")]
public sealed class BalancesController(
    GetCurrentBalanceQueryHandler getCurrentBalanceHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BalanceResponse>> GetCurrentBalance(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var result = await getCurrentBalanceHandler.HandleAsync(
            new GetCurrentBalanceQuery(), cancellationToken);

        return Ok(BalanceResponse.Map(result));
    }
}
