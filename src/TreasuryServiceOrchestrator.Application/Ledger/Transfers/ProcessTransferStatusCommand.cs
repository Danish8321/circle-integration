namespace TreasuryServiceOrchestrator.Application.Ledger.Transfers;

/// <summary>
/// Webhook-driven: <c>Status</c> is the raw provider literal (<c>pending | running | complete |
/// failed</c>), mapped through <see cref="TransferStatusMapper"/>.
/// </summary>
public sealed record ProcessTransferStatusCommand(
    string CircleTransferId,
    string Status);
