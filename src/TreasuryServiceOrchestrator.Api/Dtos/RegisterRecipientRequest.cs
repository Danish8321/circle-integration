namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record RegisterRecipientRequest(string Chain, string Address, string Label);
