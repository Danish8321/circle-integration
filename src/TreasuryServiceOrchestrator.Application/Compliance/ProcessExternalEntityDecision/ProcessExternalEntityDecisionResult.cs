using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.ProcessExternalEntityDecision;

public sealed record ProcessExternalEntityDecisionResult(
    Guid SubAccountId,
    string ClientCompanyId,
    SubAccountLifecycleState LifecycleState);
