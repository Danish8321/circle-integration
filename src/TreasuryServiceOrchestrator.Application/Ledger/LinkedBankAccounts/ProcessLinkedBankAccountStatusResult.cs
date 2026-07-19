
namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

public sealed record ProcessLinkedBankAccountStatusResult(
    Guid LinkedBankAccountId,
    LinkedBankAccountStatus Status);
