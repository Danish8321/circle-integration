using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class GetCurrentBalanceQueryHandler(
    IFundAccountRepository fundAccounts,
    ICallerContext callerContext,
    TimeProvider timeProvider)
{
    public async Task<GetCurrentBalanceResult> HandleAsync(
        GetCurrentBalanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller has no balance to look up.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var fundAccount = await fundAccounts.FindByClientCompanyIdAsync(
            callerContext.CallerId, cancellationToken);

        // Ratified 2026-07-17: no FundAccount record yet means no deposits have posted, so the
        // default balance is Money.Zero("USDC") — every funded account in this product is
        // USDC — not an exception or null.
        if (fundAccount is null)
        {
            return new GetCurrentBalanceResult(
                callerContext.CallerId, Money.Zero("USDC"), timeProvider.GetUtcNow().UtcDateTime);
        }

        return new GetCurrentBalanceResult(
            callerContext.CallerId, fundAccount.Balance, fundAccount.UpdatedAtUtc);
    }
}
