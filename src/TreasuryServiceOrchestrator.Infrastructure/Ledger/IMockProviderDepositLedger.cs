using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

/// <summary>
/// Mock-only seam for reconciliation testing (docs/features/05-reliability-and-error-handling.md
/// §7.7) — no production adapter exists, so this stays in the Infrastructure mock-provider
/// namespace rather than an Application port. <see cref="SeedAsync"/> is the test-only entry
/// point for injecting a "phantom" provider deposit that never produced a webhook.
/// </summary>
public interface IMockProviderDepositLedger
{
    Task SeedAsync(ProviderDepositRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<ProviderDepositRecord>> ListAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken ct = default);
}
