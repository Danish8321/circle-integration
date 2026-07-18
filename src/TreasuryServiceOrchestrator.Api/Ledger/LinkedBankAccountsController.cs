using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/linked-bank-accounts")]
public sealed class LinkedBankAccountsController(
    ListLinkedBankAccountsQueryHandler listLinkedBankAccountsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LinkedBankAccountResponse>>> ListLinkedBankAccounts(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var results = await listLinkedBankAccountsHandler.HandleAsync(
            new ListLinkedBankAccountsQuery(subAccountId), cancellationToken);

        return Ok(results.Select(LinkedBankAccountResponse.Map).ToList());
    }
}
