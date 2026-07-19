using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

/// <summary>
/// Maps raw Circle bank-account/wire status literals (<c>pending | complete | failed</c>) to the
/// canonical <see cref="LinkedBankAccountStatus"/>. Unlike <c>RecipientStatusMapper</c>, this is
/// a closed vocabulary per the plan (docs/features/08-banking-and-wire-instructions.md): an
/// unrecognized literal is a data-integrity problem, not a forward-compat case to shrug off, so
/// this method throws rather than silently defaulting.
/// </summary>
public static class LinkedBankAccountStatusMapper
{
    public static LinkedBankAccountStatus Map(string rawStatus)
    {
        return rawStatus.Trim().ToLowerInvariant() switch
        {
            "pending" => LinkedBankAccountStatus.Pending,
            "complete" => LinkedBankAccountStatus.Active,
            "failed" => LinkedBankAccountStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(rawStatus), rawStatus, $"Unrecognized linked bank account status literal '{rawStatus}'."),
        };
    }
}
