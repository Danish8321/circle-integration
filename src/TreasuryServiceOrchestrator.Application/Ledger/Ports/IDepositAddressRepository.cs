using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IDepositAddressRepository
{
    Task AddAsync(DepositAddress depositAddress, CancellationToken cancellationToken = default);

    Task<DepositAddress?> FindAsync(
        Guid subAccountId, string chain, string currency, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DepositAddress>> ListForSubAccountAsync(
        Guid subAccountId, PageRequest pageRequest, CancellationToken cancellationToken = default);
}
