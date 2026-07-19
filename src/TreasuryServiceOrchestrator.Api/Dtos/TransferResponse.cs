using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record TransferResponse(
    Guid Id,
    Guid SubAccountId,
    Guid RecipientId,
    Money Amount,
    string? CircleTransferId,
    TransferStatus Status,
    string? FailureReason,
    DateTime CreatedAtUtc)
{
    public static TransferResponse Map(TransferResult result) => new(
        result.Id,
        result.SubAccountId,
        result.RecipientId,
        result.Amount,
        result.CircleTransferId,
        result.Status,
        result.FailureReason,
        result.CreatedAtUtc);
}
