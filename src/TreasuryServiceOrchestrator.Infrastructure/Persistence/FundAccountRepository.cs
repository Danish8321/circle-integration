using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class FundAccountRepository(TreasuryServiceOrchestratorDbContext dbContext) : IFundAccountRepository
{
    public async Task<FundAccount?> FindByClientCompanyIdAsync(
        string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.FundAccounts
            .FirstOrDefaultAsync(x => x.ClientCompanyId == clientCompanyId, cancellationToken);
    }

    public async Task AddAsync(FundAccount fundAccount, CancellationToken cancellationToken = default)
    {
        await dbContext.FundAccounts.AddAsync(fundAccount, cancellationToken);
    }

    public async Task<IReadOnlyList<FundAccount>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        // Background snapshot pass: spans all tenants with no HTTP caller. Bypass the global
        // tenant query filter (INV7 backstop) — the caller re-establishes per-tenant identity
        // before any tenant-scoped read/write.
        return await dbContext.FundAccounts.IgnoreQueryFilters().ToListAsync(cancellationToken);
    }
}
