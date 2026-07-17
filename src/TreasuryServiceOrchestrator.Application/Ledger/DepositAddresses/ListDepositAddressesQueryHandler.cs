using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Ledger.DepositAddresses;

public sealed class ListDepositAddressesQueryHandler(
    IDepositAddressRepository depositAddresses,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<GenerateDepositAddressResult>> HandleAsync(
        ListDepositAddressesQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot list any sub-account's addresses.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var listed = await depositAddresses.ListForSubAccountAsync(query.SubAccountId, cancellationToken);

        return listed
            .Select(depositAddress => new GenerateDepositAddressResult(
                depositAddress.Id,
                depositAddress.SubAccountId,
                depositAddress.Chain,
                depositAddress.Currency,
                depositAddress.Address,
                depositAddress.CreatedAtUtc))
            .ToList();
    }
}
