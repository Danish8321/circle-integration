using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class SubAccountRepository(TreasuryServiceOrchestratorDbContext dbContext) : ISubAccountRepository
{
    public async Task AddAsync(SubAccount subAccount, CancellationToken cancellationToken = default)
    {
        await dbContext.SubAccounts.AddAsync(subAccount, cancellationToken);
    }

    public async Task<SubAccount?> GetByClientCompanyIdAsync(
        string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.SubAccounts
            .FirstOrDefaultAsync(x => x.ClientCompanyId == clientCompanyId, cancellationToken);
    }

    public async Task<SubAccount?> GetByCircleWalletIdAsync(
        string circleWalletId, CancellationToken cancellationToken = default)
    {
        return await dbContext.SubAccounts
            .FirstOrDefaultAsync(x => x.CircleWalletId == circleWalletId, cancellationToken);
    }

    public async Task<IReadOnlyList<SubAccount>> ListAsync(
        SubAccountLifecycleState? lifecycleState = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.SubAccounts.AsQueryable();
        if (lifecycleState is not null)
        {
            query = query.Where(x => x.LifecycleState == lifecycleState);
        }

        return await query
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
