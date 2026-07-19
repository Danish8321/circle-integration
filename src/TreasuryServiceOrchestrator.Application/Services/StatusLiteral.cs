namespace TreasuryServiceOrchestrator.Application.Services;

/// <summary>
/// Shared trim/case-fold convention for the status mappers in this folder (Transfer, Recipient,
/// LinkedBankAccount, EntityRegistration) — they map a raw provider status literal to a
/// canonical domain enum. Whether an unrecognized literal throws or logs-and-defaults is each
/// mapper's own policy (closed vs. open vocabulary); only the normalization step is shared.
/// </summary>
internal static class StatusLiteral
{
    public static string Normalize(string rawStatus) => rawStatus.Trim().ToLowerInvariant();
}
