using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ProcessTransferStatusResult(
    Guid TransferId,
    TransferStatus Status);
