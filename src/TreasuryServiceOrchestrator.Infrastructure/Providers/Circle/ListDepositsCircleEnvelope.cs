using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

/// <summary>
/// Response shape for <c>GET /v1/businessAccount/deposits</c> — fiat wire deposits only (docs/
/// features/05-reliability-and-error-handling.md §7.2). Consumed by
/// <see cref="CircleMintGateway.ListRecentDepositsAsync"/>.
/// </summary>
public sealed class ListDepositsCircleEnvelope
{
    [JsonPropertyName("data")]
    public IList<ListDepositsCircleDeposit> Data { get; set; } = [];
}
