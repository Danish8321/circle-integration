using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ProcessExternalEntityDecisionResult(
    Guid SubAccountId,
    string ClientCompanyId,
    SubAccountLifecycleState LifecycleState);
