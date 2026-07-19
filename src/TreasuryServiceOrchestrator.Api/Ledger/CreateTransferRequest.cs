using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record CreateTransferRequest([property: JsonRequired] Guid RecipientId, Money Amount);
