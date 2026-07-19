using System.Text.Json.Serialization;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record CreateRedemptionRequest([property: JsonRequired] Guid LinkedBankAccountId, Money GrossAmount);
