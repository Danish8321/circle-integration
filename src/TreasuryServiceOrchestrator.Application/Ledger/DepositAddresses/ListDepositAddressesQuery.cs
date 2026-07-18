using TreasuryServiceOrchestrator.Application.Shared;

namespace TreasuryServiceOrchestrator.Application.Ledger.DepositAddresses;

public sealed record ListDepositAddressesQuery(Guid SubAccountId, PageRequest? PageRequest = null);
