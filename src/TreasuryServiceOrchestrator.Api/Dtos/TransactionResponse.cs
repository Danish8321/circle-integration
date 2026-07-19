using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record TransactionResponse(
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
    public static TransactionResponse Map(TransactionResult result) => new(
        result.TransactionId,
        result.SubAccountId,
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
