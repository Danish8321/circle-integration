namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

/// <summary>
/// Webhook-driven: <c>Status</c> is the raw provider literal (<c>pending | complete | failed</c>),
/// mapped through <see cref="LinkedBankAccountStatusMapper"/>.
/// </summary>
public sealed record ProcessLinkedBankAccountStatusCommand(
    string CircleBankAccountId,
    string Status);
