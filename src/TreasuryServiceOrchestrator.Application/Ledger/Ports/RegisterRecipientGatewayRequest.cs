namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public sealed record RegisterRecipientGatewayRequest(
    string Chain,
    string Address,
    string Label,
    string IdempotencyKey);
