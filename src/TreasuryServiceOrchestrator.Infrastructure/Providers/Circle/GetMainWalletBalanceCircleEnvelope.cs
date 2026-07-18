using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

/// <summary>
/// Response shape for <c>GET /v1/businessAccount/balances</c> with <c>walletId</c> omitted
/// (docs/features/12-admin-cross-tenant-views.md §3) — used only by
/// <see cref="CircleMintGateway.GetMainWalletBalanceAsync"/>.
/// </summary>
public sealed class GetMainWalletBalanceCircleEnvelope
{
    [JsonPropertyName("data")]
    public GetMainWalletBalanceCircleData Data { get; set; } = new();
}
