using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.DepositAddresses;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId}/deposit-addresses")]
public sealed class DepositAddressesController(
    GenerateDepositAddressCommandHandler generateDepositAddressHandler,
    ListDepositAddressesQueryHandler listDepositAddressesHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DepositAddressResponse>>> ListDepositAddresses(
        Guid subAccountId, CancellationToken cancellationToken)
    {
        var results = await listDepositAddressesHandler.HandleAsync(
            new ListDepositAddressesQuery(subAccountId), cancellationToken);

        return Ok(results.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<DepositAddressResponse>> GenerateDepositAddress(
        Guid subAccountId,
        [FromBody] GenerateDepositAddressRequest request,
        CancellationToken cancellationToken)
    {
        // No caller-supplied Idempotency-Key header here: the dedup key is system-generated
        // from (SubAccountId, Chain, Currency) inside the handler — deposit-address permanence
        // already gives the operation caller-facing idempotency (docs/features/09, §3.3/§7.3).
        var result = await generateDepositAddressHandler.HandleAsync(
            new GenerateDepositAddressCommand(subAccountId, request.Chain, request.Currency),
            cancellationToken);

        return CreatedAtAction(
            nameof(ListDepositAddresses),
            new { subAccountId },
            Map(result));
    }

    private static DepositAddressResponse Map(GenerateDepositAddressResult result) => new(
        result.Id,
        result.SubAccountId,
        result.Chain,
        result.Currency,
        result.Address,
        result.CreatedAtUtc);
}
