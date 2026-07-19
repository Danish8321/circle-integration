
namespace TreasuryServiceOrchestrator.Application.Ledger.Recipients;

public sealed record RegisterRecipientResult(
    Guid Id,
    Guid SubAccountId,
    string Chain,
    string Address,
    string Label,
    string? CircleRecipientId,
    RecipientStatus Status,
    DateTime CreatedAtUtc);
