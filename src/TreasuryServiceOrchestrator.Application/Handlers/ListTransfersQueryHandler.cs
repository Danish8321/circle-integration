using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class ListTransfersQueryHandler(
    ITransferRepository transfers,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<TransferResult>> HandleAsync(
        ListTransfersQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot list any sub-account's transfers.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var listed = await transfers.ListBySubAccountAsync(
            query.SubAccountId, callerContext.CallerId, query.PageRequest ?? new PageRequest(), cancellationToken);

        return listed.Select(Map).ToList();
    }

    private static TransferResult Map(Transfer transfer) => new(
        transfer.Id,
        transfer.SubAccountId,
        transfer.RecipientId,
        transfer.Amount,
        transfer.CircleTransferId,
        transfer.Status,
        transfer.FailureReason,
        transfer.CreatedAtUtc);
}
