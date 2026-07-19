
namespace TreasuryServiceOrchestrator.Application.Compliance.Ports;

public interface IEntityRegistrationRepository
{
    Task AddAsync(EntityRegistration entityRegistration, CancellationToken cancellationToken = default);

    Task<EntityRegistration?> GetLatestForSubAccountAsync(
        Guid subAccountId, CancellationToken cancellationToken = default);
}
