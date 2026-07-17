namespace TreasuryServiceOrchestrator.Api.Ledger;

public sealed record GenerateDepositAddressRequest(string Chain, string Currency);
