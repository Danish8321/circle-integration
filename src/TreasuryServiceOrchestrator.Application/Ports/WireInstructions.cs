namespace TreasuryServiceOrchestrator.Application.Ports;

// docs/features/08-banking-and-wire-instructions.md §3.2/§5 — Circle's
// GET /v1/businessAccount/banks/wires/{id}/instructions response, mapped straight through.
public sealed record WireInstructions(
    string TrackingRef,
    string BeneficiaryName,
    string BeneficiaryAddress,
    string BankName,
    string SwiftCode,
    string RoutingNumber,
    string MaskedAccountNumber,
    string Currency);
