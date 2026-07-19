namespace TreasuryServiceOrchestrator.Application.Ports;

public sealed record GeneratedDepositAddress(
    string Address,
    string Chain,
    string Currency,
    string? ProviderAddressId);
