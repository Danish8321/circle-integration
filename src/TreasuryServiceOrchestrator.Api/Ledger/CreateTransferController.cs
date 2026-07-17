using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/transfers")]
public sealed class CreateTransferController(
    CreateTransferCommandHandler createTransferHandler) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferResponse>> CreateTransfer(
        Guid subAccountId,
        [FromBody] CreateTransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Caller-supplied idempotency key (invariant 11): this is a one-shot money-moving
        // request, mirroring SubAccountsController.CreateSubAccount's header convention rather
        // than RegisterRecipientController's system-derived-key convention.
        var correlationId = HttpContext.TraceIdentifier;

        var result = await createTransferHandler.HandleAsync(
            new CreateTransferCommand(request.RecipientId, request.Amount, idempotencyKey, correlationId),
            cancellationToken);

        return CreatedAtAction(
            nameof(TransferDetailController.GetTransfer),
            "TransferDetail",
            new { subAccountId, transferId = result.Id },
            TransferResponse.Map(result));
    }
}
