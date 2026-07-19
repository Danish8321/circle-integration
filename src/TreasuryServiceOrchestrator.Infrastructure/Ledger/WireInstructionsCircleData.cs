using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class WireInstructionsCircleData
{
    [JsonPropertyName("trackingRef")]
    public string TrackingRef { get; set; } = string.Empty;

    [JsonPropertyName("beneficiary")]
    public WireInstructionsCircleBeneficiary Beneficiary { get; set; } = new();

    [JsonPropertyName("beneficiaryBank")]
    public WireInstructionsCircleBeneficiaryBank BeneficiaryBank { get; set; } = new();
}
