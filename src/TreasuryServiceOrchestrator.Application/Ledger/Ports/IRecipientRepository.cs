using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IRecipientRepository
{
    Task AddAsync(Recipient recipient, CancellationToken cancellationToken = default);

    // Tenant-scoped at the data-access layer per CLAUDE.md invariant 7: both the recipient id
    // and the caller's ClientCompanyId are required, so cross-tenant reads are structurally
    // impossible rather than filtered after the fact.
    Task<Recipient?> FindByIdAsync(
        Guid recipientId, string clientCompanyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Recipient>> ListForSubAccountAsync(
        Guid subAccountId, string clientCompanyId, PageRequest pageRequest, CancellationToken cancellationToken = default);

    // For webhook correlation (ticket 05.4): Circle's recipient webhooks carry only the
    // provider-side recipient id, not our tenant identity, so this lookup is not
    // tenant-scoped by parameter — the caller resolves ClientCompanyId from the returned
    // Recipient itself.
    Task<Recipient?> FindByCircleRecipientIdAsync(
        string circleRecipientId, CancellationToken cancellationToken = default);
}
