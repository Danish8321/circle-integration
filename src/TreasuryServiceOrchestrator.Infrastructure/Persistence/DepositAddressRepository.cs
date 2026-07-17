using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class DepositAddressRepository(TreasuryServiceOrchestratorDbContext dbContext) : IDepositAddressRepository
{
    public async Task AddAsync(DepositAddress depositAddress, CancellationToken cancellationToken = default)
    {
        await dbContext.DepositAddresses.AddAsync(depositAddress, cancellationToken);
    }

    public async Task<DepositAddress?> FindAsync(
        Guid subAccountId, string chain, string currency, CancellationToken cancellationToken = default)
    {
        return await dbContext.DepositAddresses
            .FirstOrDefaultAsync(
                x => x.SubAccountId == subAccountId && x.Chain == chain && x.Currency == currency,
                cancellationToken);
    }

    public async Task<IReadOnlyList<DepositAddress>> ListForSubAccountAsync(
        Guid subAccountId, CancellationToken cancellationToken = default)
    {
        return await dbContext.DepositAddresses
            .Where(x => x.SubAccountId == subAccountId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
