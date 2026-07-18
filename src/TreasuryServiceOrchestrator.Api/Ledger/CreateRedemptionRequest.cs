using System.Text.Json.Serialization;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record CreateRedemptionRequest([property: JsonRequired] Guid LinkedBankAccountId, Money GrossAmount);
