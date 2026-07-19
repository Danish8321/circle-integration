using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ProcessLinkedBankAccountStatusResult(
    Guid LinkedBankAccountId,
    LinkedBankAccountStatus Status);
