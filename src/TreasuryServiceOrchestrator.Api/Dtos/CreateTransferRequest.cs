using System.Text.Json.Serialization;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record CreateTransferRequest([property: JsonRequired] Guid RecipientId, Money Amount);
