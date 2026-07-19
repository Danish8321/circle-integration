using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record BalanceResponse(string ClientCompanyId, Money Balance, DateTime AsOfUtc)
{
    public static BalanceResponse Map(GetCurrentBalanceResult result) => new(
        result.ClientCompanyId, result.Balance, result.AsOfUtc);
}
