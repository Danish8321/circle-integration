using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/recipients")]
public sealed class RegisterRecipientController(
    RegisterRecipientCommandHandler registerRecipientHandler) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RecipientResponse>> RegisterRecipient(
        Guid subAccountId,
        [FromBody] RegisterRecipientRequest request,
        CancellationToken cancellationToken)
    {
        // No caller-supplied Idempotency-Key header here: the handler derives its own dedup key
        // from (CallerId, Chain, Address), mirroring DepositAddressesController's convention.
        var result = await registerRecipientHandler.HandleAsync(
            new RegisterRecipientCommand(subAccountId, request.Chain, request.Address, request.Label),
            cancellationToken);

        return CreatedAtAction(
            nameof(RecipientDetailController.GetRecipient),
            "RecipientDetail",
            new { subAccountId, recipientId = result.Id },
            RecipientResponse.Map(result));
    }
}
