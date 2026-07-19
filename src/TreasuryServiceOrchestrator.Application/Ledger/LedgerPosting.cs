
namespace TreasuryServiceOrchestrator.Application.Ledger;

/// <summary>
/// Posting-intent DTO passed by callers to <see cref="LedgerPostingService"/>. Not the
/// Transaction entity itself. Amount is signed: credit = positive, debit = negative.
/// </summary>
public sealed record LedgerPosting(
    Guid SubAccountId,
    string ClientCompanyId,
    TransactionType Type,
    Money Amount,
    string ProviderReferenceId,
    DepositSourceType? DepositSourceType,
    string CorrelationId);
