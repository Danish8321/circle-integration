using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ports;

public interface ITransferRepository
{
    Task AddAsync(Transfer transfer, CancellationToken cancellationToken = default);

    // Tenant-scoped at the data-access layer per CLAUDE.md invariant 7: both the transfer id
    // and the caller's ClientCompanyId are required, so cross-tenant reads are structurally
    // impossible rather than filtered after the fact.
    Task<Transfer?> GetByIdAsync(
        Guid transferId, string clientCompanyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transfer>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, PageRequest pageRequest, CancellationToken cancellationToken = default);

    // For webhook correlation: Circle's transfer webhooks carry only the provider-side
    // transfer id, not our tenant identity, so this lookup is not tenant-scoped by parameter —
    // the caller resolves ClientCompanyId from the returned Transfer itself.
    Task<Transfer?> FindByCircleTransferIdAsync(
        string circleTransferId, CancellationToken cancellationToken = default);
}
