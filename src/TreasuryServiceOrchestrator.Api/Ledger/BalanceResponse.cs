using TreasuryServiceOrchestrator.Application.Ledger;

namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record BalanceResponse(string ClientCompanyId, Money Balance, DateTime AsOfUtc)
{
    public static BalanceResponse Map(GetCurrentBalanceResult result) => new(
        result.ClientCompanyId, result.Balance, result.AsOfUtc);
}
