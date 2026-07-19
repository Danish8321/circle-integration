
namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record ProcessDepositResult(
    Guid TransactionId,
    Guid SubAccountId,
    Money Amount,
    TransactionStatus Status,
    DateTime CreatedAtUtc);
