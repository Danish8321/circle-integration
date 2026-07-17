using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/balances/history")]
public sealed class BalanceHistoryController(
    GetBalanceHistoryQueryHandler getBalanceHistoryHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BalanceSnapshotResponse>>> GetBalanceHistory(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var results = await getBalanceHistoryHandler.HandleAsync(
            new GetBalanceHistoryQuery(subAccountId), cancellationToken);

        return Ok(results.Select(BalanceSnapshotResponse.Map).ToList());
    }
}
