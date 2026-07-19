using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Admin;

[ApiController]
[Route("v1/admin/transactions")]
public sealed class AdminTransactionsController(
    ListAllTransactionsQueryHandler listAllTransactionsHandler,
    ICallerContext callerContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminTransactionResponse>>> ListAllTransactions(
        [FromQuery] string? clientCompanyId,
        [FromQuery] TransactionType? type,
        [FromQuery] TransactionStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        // No route segment for TenantScopeResolver to arbitrate against, so the Admin gate is
        // enforced directly here rather than via TenantScopeResolver.
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var filter = new TransactionListFilter(
            clientCompanyId,
            type,
            status,
            fromUtc,
            toUtc,
            page == 0 ? 1 : page,
            pageSize == 0 ? 20 : pageSize);

        var results = await listAllTransactionsHandler.HandleAsync(
            new ListAllTransactionsQuery(filter), cancellationToken);

        return Ok(results.Select(AdminTransactionResponse.Map).ToList());
    }
}
