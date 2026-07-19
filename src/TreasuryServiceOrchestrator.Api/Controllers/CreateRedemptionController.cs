using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/redemptions")]
public sealed class CreateRedemptionController(
    CreateRedemptionCommandHandler createRedemptionHandler) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RedemptionResponse>> CreateRedemption(
        Guid subAccountId,
        [FromBody] CreateRedemptionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Caller-supplied idempotency key (invariant 11): one-shot money-moving request, mirrors
        // CreateTransferController's header convention.
        var correlationId = HttpContext.TraceIdentifier;

        var result = await createRedemptionHandler.HandleAsync(
            new CreateRedemptionCommand(
                request.LinkedBankAccountId, request.GrossAmount, idempotencyKey, correlationId),
            cancellationToken);

        return CreatedAtAction(
            nameof(RedemptionDetailController.GetRedemption),
            "RedemptionDetail",
            new { subAccountId, redemptionId = result.Id },
            RedemptionResponse.Map(result));
    }
}
