
namespace TreasuryServiceOrchestrator.Application.Ledger;

// Admin, all-tenant projection: carries ClientCompanyId (unlike TransactionResult, whose
// tenant-scoped query already pins one caller). Keeps Domain.Transaction from crossing the
// Application boundary into the Api tier (invariant 5).
public sealed record AdminTransactionResult(
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
    public static AdminTransactionResult Map(Transaction transaction) => new(
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
