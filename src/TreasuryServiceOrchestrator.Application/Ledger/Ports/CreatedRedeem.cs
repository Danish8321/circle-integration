namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

// Status is the raw provider literal, not the Domain TransferStatus enum — mapping to the
// canonical enum is owned by the handler/mapper that consumes this DTO.
public sealed record CreatedRedeem(
    string CircleRedeemId,
    string Status);
