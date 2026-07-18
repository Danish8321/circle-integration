using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

/// <summary>
/// One provider-side deposit, from either the fiat wire deposits endpoint or the on-chain
/// transfers endpoint (docs/features/05-reliability-and-error-handling.md §7.2 — the deposits
/// endpoint carries wire deposits only, on-chain arrives via transfers). Reconciliation merges
/// both into this one shape so callers never issue the two HTTP calls themselves.
/// </summary>
public sealed record ProviderDepositRecord(
    string ProviderReferenceId,
    string CircleWalletId,
    string DestinationAddress,
    Money Amount,
    DepositSourceType SourceType,
    DateTime OccurredAtUtc);
