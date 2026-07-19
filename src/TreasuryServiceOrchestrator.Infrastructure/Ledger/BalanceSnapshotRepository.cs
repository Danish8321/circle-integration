using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class BalanceSnapshotRepository(TreasuryServiceOrchestratorDbContext dbContext)
    : IBalanceSnapshotRepository
{
    public async Task AddAsync(BalanceSnapshot balanceSnapshot, CancellationToken cancellationToken = default)
    {
        await dbContext.BalanceSnapshots.AddAsync(balanceSnapshot, cancellationToken);
    }

    public async Task<IReadOnlyList<BalanceSnapshot>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.BalanceSnapshots
            .Where(x => x.SubAccountId == subAccountId && x.ClientCompanyId == clientCompanyId)
            .OrderBy(x => x.CapturedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<BalanceSnapshot?> GetLatestAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.BalanceSnapshots
            .Where(x => x.SubAccountId == subAccountId && x.ClientCompanyId == clientCompanyId)
            .OrderByDescending(x => x.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
