using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ProcessRecipientDecisionResult(
    Guid RecipientId,
    RecipientStatus Status);
