using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

/// <summary>
/// Posts a single signed ledger movement: persists a Transaction, adjusts the FundAccount
/// balance, and records a BalanceSnapshot. Single method per the ratified design (ticket 12) —
/// not split CreditAsync/DebitAsync. Callers with a full reserve -> gateway -> complete flow
/// invoke this as a helper step, not as their own two-SaveChanges flow.
/// </summary>
public sealed class LedgerPostingService(
    ITransactionRepository transactions,
    IBalanceSnapshotRepository balanceSnapshots,
    IFundAccountRepository fundAccounts,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Transaction> PostAsync(LedgerPosting posting, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(posting);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var transaction = Transaction.Create(
            posting.SubAccountId,
            posting.ClientCompanyId,
            posting.Type,
            TransactionStatus.Complete,
            posting.Amount,
            posting.ProviderReferenceId,
            posting.DepositSourceType,
            failureReason: null,
            posting.CorrelationId,
            nowUtc);

        await transactions.AddAsync(transaction, ct);

        var fundAccount = await fundAccounts.FindByClientCompanyIdAsync(posting.ClientCompanyId, ct);
        if (fundAccount is null)
        {
            fundAccount = FundAccount.Create(
                posting.ClientCompanyId, Money.Zero(posting.Amount.CurrencyCode), nowUtc);
            await fundAccounts.AddAsync(fundAccount, ct);
        }

        var newBalance = new Money(
            fundAccount.Balance.Amount + posting.Amount.Amount, fundAccount.Balance.CurrencyCode);
        fundAccount.ApplyBalance(newBalance, nowUtc);

        var snapshot = BalanceSnapshot.Create(
            posting.SubAccountId,
            posting.ClientCompanyId,
            newBalance,
            BalanceSnapshotReason.PostMutation,
            nowUtc);
        await balanceSnapshots.AddAsync(snapshot, ct);

        await unitOfWork.SaveChangesAsync(ct);

        return transaction;
    }
}
