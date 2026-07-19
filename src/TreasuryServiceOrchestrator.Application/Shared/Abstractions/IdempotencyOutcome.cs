namespace TreasuryServiceOrchestrator.Application.Shared.Abstractions;

/// <summary>
/// Result of <see cref="IIdempotencyService.TryBeginAsync"/> — the reserve step of the
/// reserve → gateway → complete flow (CLAUDE.md invariant 11). A fresh reservation staged an
/// <c>InProgress</c> record the caller must commit before calling the provider; a replay hands
/// back the already-completed result; an in-flight retry means a prior attempt reserved but never
/// completed (e.g. crashed after the gateway call) and the caller must re-drive the work.
/// A key reused with a different request payload throws instead of returning an outcome.
/// </summary>
public abstract record IdempotencyOutcome
{
    private IdempotencyOutcome() { }

    /// <summary>New reservation staged; caller commits it (SaveChanges #1), then does the work.</summary>
    public sealed record Started : IdempotencyOutcome;

    /// <summary>Prior attempt completed with this exact payload; return the cached result verbatim.</summary>
    public sealed record Replay(string ResultJson) : IdempotencyOutcome;

    /// <summary>Prior attempt reserved but never completed; re-drive the work to finish it.</summary>
    public sealed record InFlightRetry : IdempotencyOutcome;
}
