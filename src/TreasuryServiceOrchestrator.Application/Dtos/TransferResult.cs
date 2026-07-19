using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record TransferResult(
    Guid Id,
    Guid SubAccountId,
    Guid RecipientId,
    Money Amount,
    string? CircleTransferId,
    TransferStatus Status,
    string? FailureReason,
    DateTime CreatedAtUtc);
