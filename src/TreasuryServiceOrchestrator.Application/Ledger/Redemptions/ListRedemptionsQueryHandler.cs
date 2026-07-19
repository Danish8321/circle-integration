using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

public sealed class ListRedemptionsQueryHandler(
    IRedeemRequestRepository redeemRequests,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<RedemptionResult>> HandleAsync(
        ListRedemptionsQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot list any sub-account's redemptions.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var listed = await redeemRequests.ListBySubAccountAsync(
            query.SubAccountId, callerContext.CallerId, query.PageRequest ?? new PageRequest(), cancellationToken);

        return listed.Select(Map).ToList();
    }

    private static RedemptionResult Map(RedeemRequest redeemRequest) => new(
        redeemRequest.Id,
        redeemRequest.SubAccountId,
        redeemRequest.LinkedBankAccountId,
        redeemRequest.GrossAmount,
        redeemRequest.Fees,
        redeemRequest.NetAmount,
        redeemRequest.CircleRedeemId,
        redeemRequest.Status,
        redeemRequest.FailureReason,
        redeemRequest.CreatedAtUtc);
}
