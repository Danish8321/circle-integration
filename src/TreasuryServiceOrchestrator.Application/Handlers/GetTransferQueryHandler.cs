using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class GetTransferQueryHandler(
    ITransferRepository transfers,
    ICallerContext callerContext)
{
    public async Task<TransferResult> HandleAsync(
        GetTransferQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot look up any transfer.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var transfer = await transfers.GetByIdAsync(
            query.TransferId, callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No transfer '{query.TransferId}'.");

        return Map(transfer);
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
