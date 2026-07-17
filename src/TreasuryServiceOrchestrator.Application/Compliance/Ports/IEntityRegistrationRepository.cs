using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.Ports;

public interface IEntityRegistrationRepository
{
    Task AddAsync(EntityRegistration entityRegistration, CancellationToken cancellationToken = default);
}
