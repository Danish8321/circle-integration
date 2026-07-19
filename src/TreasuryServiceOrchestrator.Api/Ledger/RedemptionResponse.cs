using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record RedemptionResponse(
    Guid Id,
    Guid SubAccountId,
    Guid LinkedBankAccountId,
    Money GrossAmount,
    Money? Fees,
    Money? NetAmount,
    string? CircleRedeemId,
    TransferStatus Status,
    string? FailureReason,
    DateTime CreatedAtUtc)
{
    public static RedemptionResponse Map(RedemptionResult result) => new(
        result.Id,
        result.SubAccountId,
        result.LinkedBankAccountId,
        result.GrossAmount,
        result.Fees,
        result.NetAmount,
        result.CircleRedeemId,
        result.Status,
        result.FailureReason,
        result.CreatedAtUtc);
}
