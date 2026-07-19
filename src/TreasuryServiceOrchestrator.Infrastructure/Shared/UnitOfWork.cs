using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.Infrastructure.Shared;

public sealed class UnitOfWork(TreasuryServiceOrchestratorDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
