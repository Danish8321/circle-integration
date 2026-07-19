using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/recipients")]
public sealed class RecipientsController(
    ListRecipientsQueryHandler listRecipientsHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipientResponse>>> ListRecipients(
        Guid subAccountId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var pageRequest = new PageRequest(page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize);
        var results = await listRecipientsHandler.HandleAsync(
            new ListRecipientsQuery(subAccountId, pageRequest), cancellationToken);

        return Ok(results.Select(RecipientResponse.Map).ToList());
    }
}
