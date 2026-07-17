namespace TreasuryServiceOrchestrator.Application.Shared.Abstractions;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
