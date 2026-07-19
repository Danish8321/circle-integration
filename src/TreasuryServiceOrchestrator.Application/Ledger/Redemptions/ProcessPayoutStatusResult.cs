using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

public sealed record ProcessPayoutStatusResult(
    Guid RedeemRequestId,
    TransferStatus Status);
