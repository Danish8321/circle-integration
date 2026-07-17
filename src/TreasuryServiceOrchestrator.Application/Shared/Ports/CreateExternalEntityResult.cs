namespace TreasuryServiceOrchestrator.Application.Shared.Ports;

public sealed record CreateExternalEntityResult(
    string WalletId,
    string ComplianceState,
    string BusinessName,
    string BusinessUniqueIdentifier);
