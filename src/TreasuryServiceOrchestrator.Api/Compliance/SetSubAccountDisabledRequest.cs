using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Compliance;

public sealed record SetSubAccountDisabledRequest([property: JsonRequired] bool Disabled);
