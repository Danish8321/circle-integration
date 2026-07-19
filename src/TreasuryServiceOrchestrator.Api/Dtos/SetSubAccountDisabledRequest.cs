using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record SetSubAccountDisabledRequest([property: JsonRequired] bool Disabled);
