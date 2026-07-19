
namespace TreasuryServiceOrchestrator.Application.Ledger.Recipients;

/// <summary>
/// Maps raw Circle recipient status literals to the canonical <see cref="RecipientStatus"/>.
/// Two distinct provider vocabularies feed this: the REST create-response enum
/// (<c>pending_verification | verification_succeeded | active</c>) and the
/// <c>addressBookRecipients</c> webhook vocabulary (<c>pending | inactive | active | denied</c>).
/// Rule: <c>active</c> -> Active; <c>denied</c> -> Denied; anything else, including any
/// unrecognized/future literal, -> PendingApproval. This method must NEVER throw — an unknown
/// literal is logged and safely defaulted rather than dead-lettering the caller.
/// <c>pending_approval</c> (with an underscore) is not a real Circle literal on either
/// vocabulary; it is this codebase's own canonical enum member name only.
/// </summary>
public static class RecipientStatusMapper
{
    public static RecipientStatus Map(string rawStatus, Action<string>? logUnknown = null)
    {
        return rawStatus.Trim().ToLowerInvariant() switch
        {
            "active" => RecipientStatus.Active,
            "denied" => RecipientStatus.Denied,
            "pending" or "inactive" => RecipientStatus.PendingApproval,
            "pending_verification" or "verification_succeeded" => RecipientStatus.PendingApproval,
            _ => LogUnknownAndFallBack(rawStatus, logUnknown),
        };
    }

    private static RecipientStatus LogUnknownAndFallBack(string rawStatus, Action<string>? logUnknown)
    {
        logUnknown?.Invoke(
            $"Unrecognized recipient status literal '{rawStatus}'; defaulting to PendingApproval.");
        return RecipientStatus.PendingApproval;
    }
}
