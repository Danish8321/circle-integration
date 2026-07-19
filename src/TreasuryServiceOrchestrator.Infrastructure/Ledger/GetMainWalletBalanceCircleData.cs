using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class GetMainWalletBalanceCircleData
{
    [JsonPropertyName("available")]
    public IList<GetMainWalletBalanceCircleAmount> Available { get; set; } = [];
}
