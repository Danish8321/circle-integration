
namespace TreasuryServiceOrchestrator.Application.Ledger.Recipients;

public sealed record ProcessRecipientDecisionResult(
    Guid RecipientId,
    RecipientStatus Status);
