
namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

/// <summary>
/// Request to move funds out via a registered recipient. Maps to Circle's
/// <c>POST /v1/businessAccount/transfers</c>: <see cref="DestinationRecipientId"/> is our
/// recipient's Circle-side id and is sent as the destination's <c>addressId</c> field, i.e.
/// <c>destination: {type: "verified_blockchain", addressId: DestinationRecipientId}</c>. No
/// Travel Rule originator name/address fields exist on this endpoint (CLAUDE.md invariant 12) —
/// do not add any.
/// </summary>
public sealed record CreateTransferGatewayRequest(
    string DestinationRecipientId,
    Money Amount,
    string IdempotencyKey);
