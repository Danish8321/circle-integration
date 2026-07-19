using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ProcessDepositResult(
    Guid TransactionId,
    Guid SubAccountId,
    Money Amount,
    TransactionStatus Status,
    DateTime CreatedAtUtc);
