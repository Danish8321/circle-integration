using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record LinkedBankAccountResponse(
    Guid Id,
    Guid SubAccountId,
    string BeneficiaryName,
    string BankName,
    string? CircleBankAccountId,
    LinkedBankAccountStatus Status,
    DateTime CreatedAtUtc)
{
    public static LinkedBankAccountResponse Map(LinkedBankAccountResult result) => new(
        result.Id,
        result.SubAccountId,
        result.BeneficiaryName,
        result.BankName,
        result.CircleBankAccountId,
        result.Status,
        result.CreatedAtUtc);
}
