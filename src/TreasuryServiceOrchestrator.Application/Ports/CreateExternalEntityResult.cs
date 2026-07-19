namespace TreasuryServiceOrchestrator.Application.Ports;

public sealed record CreateExternalEntityResult(
    string WalletId,
    string ComplianceState,
    string BusinessName,
    string BusinessUniqueIdentifier);
