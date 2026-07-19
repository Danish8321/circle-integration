namespace TreasuryServiceOrchestrator.Application.Ports;

public sealed record RegisterRecipientGatewayRequest(
    string Chain,
    string Address,
    string Label,
    string IdempotencyKey);
