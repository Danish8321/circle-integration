using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Shared;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/linked-bank-accounts")]
public sealed class LinkedBankAccountsController(
    ListLinkedBankAccountsQueryHandler listLinkedBankAccountsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LinkedBankAccountResponse>>> ListLinkedBankAccounts(
        Guid subAccountId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var pageRequest = new PageRequest(page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize);
        var results = await listLinkedBankAccountsHandler.HandleAsync(
            new ListLinkedBankAccountsQuery(subAccountId, pageRequest), cancellationToken);

        return Ok(results.Select(LinkedBankAccountResponse.Map).ToList());
    }
}
