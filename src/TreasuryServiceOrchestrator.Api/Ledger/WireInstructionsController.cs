using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/linked-bank-accounts/{linkedBankAccountId:guid}/wire-instructions")]
public sealed class WireInstructionsController(
    GetWireInstructionsQueryHandler getWireInstructionsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WireInstructions>> GetWireInstructions(
        Guid subAccountId, Guid linkedBankAccountId, CancellationToken cancellationToken)
    {
        var result = await getWireInstructionsHandler.HandleAsync(
            new GetWireInstructionsQuery(linkedBankAccountId), cancellationToken);

        return Ok(result);
    }
}
