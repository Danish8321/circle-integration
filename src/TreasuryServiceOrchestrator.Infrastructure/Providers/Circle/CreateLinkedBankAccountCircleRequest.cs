using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

// docs/features/08-banking-and-wire-instructions.md §7 — US wire-creation body
// (WireCreationRequestUs). IBAN/non-IBAN-non-US schemas remain out of scope (US-only, Phase 1).
public sealed class CreateLinkedBankAccountCircleRequest
{
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonPropertyName("accountNumber")]
    public string AccountNumber { get; set; } = string.Empty;

    [JsonPropertyName("routingNumber")]
    public string RoutingNumber { get; set; } = string.Empty;

    [JsonPropertyName("billingDetails")]
    public CreateLinkedBankAccountCircleBillingDetails BillingDetails { get; set; } = new();

    [JsonPropertyName("bankAddress")]
    public CreateLinkedBankAccountCircleBankAddress BankAddress { get; set; } = new();
}
