using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

public sealed record RedemptionResult(
    Guid Id,
    Guid SubAccountId,
    Guid LinkedBankAccountId,
    Money GrossAmount,
    Money? Fees,
    Money? NetAmount,
    string? CircleRedeemId,
    TransferStatus Status,
    string? FailureReason,
    DateTime CreatedAtUtc);
