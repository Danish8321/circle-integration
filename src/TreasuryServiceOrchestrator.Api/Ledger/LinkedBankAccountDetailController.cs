using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/linked-bank-accounts/{linkedBankAccountId:guid}")]
public sealed class LinkedBankAccountDetailController(
    GetLinkedBankAccountQueryHandler getLinkedBankAccountHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LinkedBankAccountResponse>> GetLinkedBankAccount(
        Guid subAccountId, Guid linkedBankAccountId, CancellationToken cancellationToken)
    {
        var result = await getLinkedBankAccountHandler.HandleAsync(
            new GetLinkedBankAccountQuery(linkedBankAccountId), cancellationToken);

        return Ok(LinkedBankAccountResponse.Map(result));
    }
}
