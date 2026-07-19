using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class GetRedemptionQueryHandler(
    IRedeemRequestRepository redeemRequests,
    ICallerContext callerContext)
{
    public async Task<RedemptionResult> HandleAsync(
        GetRedemptionQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot look up any redemption.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var redeemRequest = await redeemRequests.GetByIdAsync(
            query.RedeemRequestId, callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No redemption '{query.RedeemRequestId}'.");

        return Map(redeemRequest);
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
