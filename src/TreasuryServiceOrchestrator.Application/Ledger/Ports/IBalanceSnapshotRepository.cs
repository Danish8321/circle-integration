using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IBalanceSnapshotRepository
{
    Task AddAsync(BalanceSnapshot balanceSnapshot, CancellationToken cancellationToken = default);

    // Tenant-scoped at the data-access layer per CLAUDE.md invariant 7 — see
    // ITransactionRepository.ListBySubAccountAsync.
    Task<IReadOnlyList<BalanceSnapshot>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default);

    // Tenant-scoped like ListBySubAccountAsync; used by GetMasterAccountSummaryQueryHandler
    // (Admin module) once per sub-account to sum latest snapshots across all tenants —
    // docs/features/12-admin-cross-tenant-views.md §2.4.
    Task<BalanceSnapshot?> GetLatestAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default);
}
