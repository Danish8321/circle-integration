using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record GetCurrentBalanceResult(
    string ClientCompanyId,
    Money Balance,
    DateTime AsOfUtc);
