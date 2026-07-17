using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Api.Compliance;

[ApiController]
[Route("sub-accounts")]
public sealed class SubAccountsController(
    CreateSubAccountHandler createSubAccountHandler, ICallerContext callerContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CreateSubAccountResponse>> CreateSubAccount(
        [FromBody] CreateSubAccountRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!callerContext.IsAdmin)
        {
            return Problem(
                title: "Only Admin may create sub-accounts.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var correlationId = HttpContext.TraceIdentifier;

        var result = await createSubAccountHandler.HandleAsync(
            new CreateSubAccountCommand(
                request.ClientCompanyId,
                request.BusinessName,
                request.BusinessUniqueIdentifier,
                request.IdentifierIssuingCountryCode,
                request.Country,
                request.State,
                request.City,
                request.Postcode,
                request.StreetName,
                request.BuildingNumber,
                idempotencyKey,
                correlationId),
            cancellationToken);

        return CreatedAtAction(
            nameof(CreateSubAccount),
            new { result.SubAccountId },
            new CreateSubAccountResponse(
                result.SubAccountId, result.ClientCompanyId, result.CircleWalletId, result.LifecycleState.ToString()));
    }
}
