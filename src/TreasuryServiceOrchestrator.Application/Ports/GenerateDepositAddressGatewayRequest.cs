namespace TreasuryServiceOrchestrator.Application.Ports;

public sealed record GenerateDepositAddressGatewayRequest(
    string Chain,
    string Currency,
    string IdempotencyKey);
