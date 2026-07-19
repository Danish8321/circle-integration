using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record CreateRedemptionRequest([property: JsonRequired] Guid LinkedBankAccountId, Money GrossAmount);
