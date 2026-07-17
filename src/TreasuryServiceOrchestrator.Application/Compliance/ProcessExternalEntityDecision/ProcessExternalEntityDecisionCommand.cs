namespace TreasuryServiceOrchestrator.Application.Compliance.ProcessExternalEntityDecision;

public sealed record ProcessExternalEntityDecisionCommand(
    string CircleWalletId,
    string ComplianceState,
    string CorrelationId);
