using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Transfers;

/// <summary>
/// Maps raw Circle transfer status literals to the canonical <see cref="TransferStatus"/>.
/// Provider progression is <c>pending -> running -> complete | failed</c>; the product collapses
/// the <c>running</c> intermediate into <c>Pending</c> (no separate product state). Mirrors
/// <c>RecipientStatusMapper</c>'s never-throws-log-and-default shape: an unrecognized literal is
/// logged and defaulted to <see cref="TransferStatus.Pending"/> rather than throwing.
/// </summary>
public static class TransferStatusMapper
{
    public static TransferStatus Map(string rawStatus, Action<string>? logUnknown = null)
    {
        return rawStatus.Trim().ToLowerInvariant() switch
        {
            "pending" or "running" => TransferStatus.Pending,
            "complete" => TransferStatus.Complete,
            "failed" => TransferStatus.Failed,
            _ => LogUnknownAndFallBack(rawStatus, logUnknown),
        };
    }

    private static TransferStatus LogUnknownAndFallBack(string rawStatus, Action<string>? logUnknown)
    {
        logUnknown?.Invoke(
            $"Unrecognized transfer status literal '{rawStatus}'; defaulting to Pending.");
        return TransferStatus.Pending;
    }
}
