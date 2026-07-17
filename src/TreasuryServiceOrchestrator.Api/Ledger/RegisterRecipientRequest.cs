namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record RegisterRecipientRequest(string Chain, string Address, string Label);
