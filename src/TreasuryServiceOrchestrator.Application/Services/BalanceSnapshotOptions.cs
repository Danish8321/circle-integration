namespace TreasuryServiceOrchestrator.Application.Services;

/// <summary>
/// Bound from an <c>appsettings.json</c> "BalanceSnapshot" section (Ticket 18.2), consumed by
/// both <see cref="ScheduledBalanceSnapshotService"/> and the Infrastructure background service
/// that polls it. See docs/features/04-ledger-and-balances.md.
/// </summary>
public sealed class BalanceSnapshotOptions
{
    public int IntervalSeconds { get; set; } = 3600;
}
