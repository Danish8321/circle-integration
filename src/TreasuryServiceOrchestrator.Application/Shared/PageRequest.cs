namespace TreasuryServiceOrchestrator.Application.Shared;

/// <summary>
/// Common pagination request shape, mirroring <see cref="Ledger.TransactionListFilter"/>'s
/// Page/PageSize field names and defaults.
/// </summary>
public sealed record PageRequest(int Page = 1, int PageSize = 20);
