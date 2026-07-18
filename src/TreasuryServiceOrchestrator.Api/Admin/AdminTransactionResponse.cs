using TreasuryServiceOrchestrator.Domain;

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
    public static AdminTransactionResponse Map(Transaction transaction) => new(
        transaction.Id,
        transaction.SubAccountId,
        transaction.ClientCompanyId,
        transaction.Type,
        transaction.Status,
        transaction.Amount,
        transaction.ProviderReferenceId,
        transaction.DepositSourceType,
        transaction.FailureReason,
        transaction.CorrelationId,
        transaction.CreatedAtUtc,
        transaction.UpdatedAtUtc);
}
