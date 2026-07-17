using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/recipients")]
public sealed class RecipientsController(
    ListRecipientsQueryHandler listRecipientsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipientResponse>>> ListRecipients(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var results = await listRecipientsHandler.HandleAsync(
            new ListRecipientsQuery(subAccountId), cancellationToken);

        return Ok(results.Select(RecipientResponse.Map).ToList());
    }
}
