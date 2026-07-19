
namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

public sealed record LinkedBankAccountResult(
    Guid Id,
    Guid SubAccountId,
    string BeneficiaryName,
    string BankName,
    string? CircleBankAccountId,
    LinkedBankAccountStatus Status,
    DateTime CreatedAtUtc);
