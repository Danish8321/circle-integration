using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IBalanceSnapshotRepository
{
    Task AddAsync(BalanceSnapshot balanceSnapshot, CancellationToken cancellationToken = default);

    // Tenant-scoped at the data-access layer per CLAUDE.md invariant 7 — see
    // ITransactionRepository.ListBySubAccountAsync.
    Task<IReadOnlyList<BalanceSnapshot>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default);
}
