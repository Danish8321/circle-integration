using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record TransactionResult(
    Guid TransactionId,
    Guid SubAccountId,
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
    public static TransactionResult Map(Transaction transaction) => new(
        transaction.Id,
        transaction.SubAccountId,
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
