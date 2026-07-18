using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
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
        Guid subAccountId, PageRequest pageRequest, CancellationToken cancellationToken = default)
    {
        var page = pageRequest.Page <= 0 ? 1 : pageRequest.Page;
        var pageSize = pageRequest.PageSize <= 0 ? 20 : pageRequest.PageSize;

        return await dbContext.DepositAddresses
            .Where(x => x.SubAccountId == subAccountId)
            .OrderBy(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
}
