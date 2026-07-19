using TreasuryServiceOrchestrator.Application.Ledger;

namespace TreasuryServiceOrchestrator.Api.Admin;

// Admin-only, all-tenant response: includes ClientCompanyId (unlike TransactionResponse, whose
// tenant-scoped route already pins the caller to a single ClientCompanyId).
public sealed record AdminTransactionResponse(
    Guid TransactionId,
    Guid SubAccountId,
    string ClientCompanyId,
    TransactionType Type,
    TransactionStatus Status,
    Money Amount,
    string ProviderReferenceId,
    DepositSourceType? DepositSourceType,
    string? FailureReason,
    string CorrelationId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static AdminTransactionResponse Map(AdminTransactionResult result) => new(
        result.TransactionId,
        result.SubAccountId,
        result.ClientCompanyId,
        result.Type,
        result.Status,
        result.Amount,
        result.ProviderReferenceId,
        result.DepositSourceType,
        result.FailureReason,
        result.CorrelationId,
        result.CreatedAtUtc,
        result.UpdatedAtUtc);
}
