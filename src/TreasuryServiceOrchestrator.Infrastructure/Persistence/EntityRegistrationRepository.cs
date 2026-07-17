using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class EntityRegistrationRepository(TreasuryServiceOrchestratorDbContext dbContext)
    : IEntityRegistrationRepository
{
    public async Task AddAsync(EntityRegistration entityRegistration, CancellationToken cancellationToken = default)
    {
        await dbContext.EntityRegistrations.AddAsync(entityRegistration, cancellationToken);
    }
}
