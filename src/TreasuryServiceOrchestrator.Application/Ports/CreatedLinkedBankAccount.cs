namespace TreasuryServiceOrchestrator.Application.Ports;

// Status is the raw provider literal, not the Domain LinkedBankAccountStatus enum — mapping
// to the canonical enum is owned by the handler/mapper that consumes this DTO.
public sealed record CreatedLinkedBankAccount(
    string CircleBankAccountId,
    string Status);
