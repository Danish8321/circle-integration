using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/recipients/{recipientId:guid}")]
public sealed class RecipientDetailController(
    GetRecipientQueryHandler getRecipientHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RecipientResponse>> GetRecipient(
        Guid subAccountId, Guid recipientId, CancellationToken cancellationToken)
    {
        var result = await getRecipientHandler.HandleAsync(
            new GetRecipientQuery(recipientId), cancellationToken);

        return Ok(RecipientResponse.Map(result));
    }
}
