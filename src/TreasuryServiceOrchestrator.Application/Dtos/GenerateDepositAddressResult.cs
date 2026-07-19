namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record GenerateDepositAddressResult(
    Guid Id,
    Guid SubAccountId,
    string Chain,
    string Currency,
    string Address,
    DateTime CreatedAtUtc);
