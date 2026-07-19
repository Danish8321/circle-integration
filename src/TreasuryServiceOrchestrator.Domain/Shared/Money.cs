namespace TreasuryServiceOrchestrator.Domain.Shared;

public sealed record Money(decimal Amount, string CurrencyCode)
{
    public static Money Zero(string currencyCode) => new(0m, currencyCode);
}
