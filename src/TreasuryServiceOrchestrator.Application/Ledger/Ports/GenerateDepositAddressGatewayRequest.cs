namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public sealed record GenerateDepositAddressGatewayRequest(
    string Chain,
    string Currency,
    string IdempotencyKey);
