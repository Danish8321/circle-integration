using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

/// <summary>
/// Response shape for <c>GET /v1/businessAccount/transfers?destinationWalletId=</c> — on-chain
/// deposits into our wallet (docs/features/05-reliability-and-error-handling.md §7.2). Consumed
/// by <see cref="CircleMintGateway.ListRecentDepositsAsync"/>; the same wire type also backs
/// outbound transfer creation elsewhere in this gateway (unrelated to this envelope).
/// </summary>
public sealed class ListTransfersCircleEnvelope
{
    [JsonPropertyName("data")]
    public IList<ListTransfersCircleTransfer> Data { get; set; } = [];
}
