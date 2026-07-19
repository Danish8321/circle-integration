namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record DepositAddressResponse(
    Guid Id,
    Guid SubAccountId,
    string Chain,
    string Currency,
    string Address,
    DateTime CreatedAtUtc);
