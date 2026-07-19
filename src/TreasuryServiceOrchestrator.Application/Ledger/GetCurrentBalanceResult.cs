using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record GetCurrentBalanceResult(
    string ClientCompanyId,
    Money Balance,
    DateTime AsOfUtc);
