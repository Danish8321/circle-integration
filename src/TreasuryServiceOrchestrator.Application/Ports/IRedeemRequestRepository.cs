using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ports;

public interface IRedeemRequestRepository
{
    Task AddAsync(RedeemRequest redeemRequest, CancellationToken cancellationToken = default);

    // Tenant-scoped at the data-access layer per CLAUDE.md invariant 7: both the redeem
    // request id and the caller's ClientCompanyId are required, so cross-tenant reads are
    // structurally impossible rather than filtered after the fact.
    Task<RedeemRequest?> GetByIdAsync(
        Guid redeemRequestId, string clientCompanyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RedeemRequest>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, PageRequest pageRequest, CancellationToken cancellationToken = default);

    // For webhook correlation: Circle's payouts webhooks carry only the provider-side redeem
    // id, not our tenant identity, so this lookup is not tenant-scoped by parameter — the
    // caller resolves ClientCompanyId from the returned RedeemRequest itself.
    Task<RedeemRequest?> FindByCircleRedeemIdAsync(
        string circleRedeemId, CancellationToken cancellationToken = default);
}
