using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Services;

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
    INotificationOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <param name="outboxEntryBuilder">
    /// Optional: builds a <see cref="NotificationOutboxEntry"/> from the just-created
    /// <see cref="Transaction"/>, staged before this method's own <c>SaveChangesAsync</c> so it
    /// commits atomically with the posting — never call this from outside after <c>PostAsync</c>
    /// returns, that lands in a later, separate commit (see ticket 09.4's atomicity proof).
    /// </param>
    /// <param name="deferCommit">
    /// When <c>true</c>, stage the posting but do <b>not</b> call <c>SaveChangesAsync</c> — the
    /// caller commits it in a single outer save together with its aggregate and the idempotency
    /// completion (reserve → gateway → complete, CLAUDE.md invariant 11 / ticket 23). When
    /// <c>false</c> (the default, for callers not wrapped in <see cref="IdempotencyExecutor"/>),
    /// the posting commits on its own here.
    /// </param>
    public async Task<Transaction> PostAsync(
        LedgerPosting posting,
        Func<Transaction, NotificationOutboxEntry>? outboxEntryBuilder,
        bool deferCommit = false,
        CancellationToken ct = default)
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

        if (outboxEntryBuilder is not null)
        {
            await outbox.AddAsync(outboxEntryBuilder(transaction), ct);
        }

        if (!deferCommit)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return transaction;
    }
}
