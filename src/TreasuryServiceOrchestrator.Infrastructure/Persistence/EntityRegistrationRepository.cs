using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class EntityRegistrationRepository(TreasuryServiceOrchestratorDbContext dbContext)
    : IEntityRegistrationRepository
{
    public async Task AddAsync(EntityRegistration entityRegistration, CancellationToken cancellationToken = default)
    {
        await dbContext.EntityRegistrations.AddAsync(entityRegistration, cancellationToken);
    }

    public async Task<EntityRegistration?> GetLatestForSubAccountAsync(
        Guid subAccountId, CancellationToken cancellationToken = default)
    {
        return await dbContext.EntityRegistrations
            .Where(x => x.SubAccountId == subAccountId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
