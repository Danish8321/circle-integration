using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ports;

public interface IFundAccountRepository
{
    Task<FundAccount?> FindByClientCompanyIdAsync(
        string clientCompanyId, CancellationToken cancellationToken = default);

    Task AddAsync(FundAccount fundAccount, CancellationToken cancellationToken = default);

    // Used by ScheduledBalanceSnapshotService (ticket 18.1) to take a point-in-time snapshot of
    // every tenant's current balance on a periodic cadence.
    Task<IReadOnlyList<FundAccount>> ListAllAsync(CancellationToken cancellationToken = default);
}
