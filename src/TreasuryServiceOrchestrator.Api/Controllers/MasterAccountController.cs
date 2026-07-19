using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/admin/master-account")]
public sealed class MasterAccountController(
    GetMasterAccountSummaryQueryHandler summaryHandler,
    ICallerContext callerContext) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<GetMasterAccountSummaryResult>> Summary(CancellationToken cancellationToken)
    {
        // No route segment for TenantScopeResolver to arbitrate against, so the Admin gate is
        // enforced directly here rather than via TenantScopeResolver — same pattern as
        // AdminTransactionsController (08.3).
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var result = await summaryHandler.HandleAsync(new GetMasterAccountSummaryQuery(), cancellationToken);

        return Ok(result);
    }
}
