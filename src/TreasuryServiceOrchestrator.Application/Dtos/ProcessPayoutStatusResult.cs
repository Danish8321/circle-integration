using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ProcessPayoutStatusResult(
    Guid RedeemRequestId,
    TransferStatus Status);
