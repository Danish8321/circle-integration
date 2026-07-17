using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IBalanceSnapshotRepository
{
    Task AddAsync(BalanceSnapshot balanceSnapshot, CancellationToken cancellationToken = default);
}
