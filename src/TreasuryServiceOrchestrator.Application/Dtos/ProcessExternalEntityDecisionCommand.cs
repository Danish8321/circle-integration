namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ProcessExternalEntityDecisionCommand(
    string CircleWalletId,
    string ComplianceState,
    string CorrelationId);
