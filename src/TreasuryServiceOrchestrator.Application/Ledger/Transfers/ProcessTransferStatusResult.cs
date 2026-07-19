using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Transfers;

public sealed record ProcessTransferStatusResult(
    Guid TransferId,
    TransferStatus Status);
