
namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ListDepositAddressesQuery(Guid SubAccountId, PageRequest? PageRequest = null);
