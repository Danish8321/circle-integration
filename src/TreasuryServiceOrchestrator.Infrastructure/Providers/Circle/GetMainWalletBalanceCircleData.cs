using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class GetMainWalletBalanceCircleData
{
    [JsonPropertyName("available")]
    public IList<GetMainWalletBalanceCircleAmount> Available { get; set; } = [];
}
