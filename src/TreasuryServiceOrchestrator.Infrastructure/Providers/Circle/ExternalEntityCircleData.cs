using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class ExternalEntityCircleData
{
    [JsonPropertyName("walletId")]
    public string WalletId { get; set; } = string.Empty;

    [JsonPropertyName("businessName")]
    public string BusinessName { get; set; } = string.Empty;

    [JsonPropertyName("businessUniqueIdentifier")]
    public string BusinessUniqueIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("complianceState")]
    public string ComplianceState { get; set; } = string.Empty;
}
