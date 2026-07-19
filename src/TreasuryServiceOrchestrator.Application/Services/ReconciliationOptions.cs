namespace TreasuryServiceOrchestrator.Application.Services;

/// <summary>
/// Bound from an <c>appsettings.json</c> "Reconciliation" section (Ticket 15.6, out of this
/// slice's scope), consumed by both <see cref="DepositReconciliationService"/> and the
/// Infrastructure background service that polls it. See
/// docs/features/05-reliability-and-error-handling.md §7.6.
/// </summary>
public sealed class ReconciliationOptions
{
    public int IntervalSeconds { get; set; } = 300;
    public int LookbackWindowMinutes { get; set; } = 1440;
}
