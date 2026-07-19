namespace TreasuryServiceOrchestrator.Application.Shared.Abstractions;

public interface IIdempotencyService
{
    /// <summary>
    /// Reserve step: stage an <c>InProgress</c> record for a fresh key, or classify an existing
    /// one. The caller commits a <see cref="IdempotencyOutcome.Started"/> reservation
    /// (SaveChanges #1) <b>before</b> calling the provider. Throws if the key was already used
    /// with a different request payload.
    /// </summary>
    Task<IdempotencyOutcome> TryBeginAsync(
        string tenantId, string idempotencyKey, string requestHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete step: flip the reserved record <c>InProgress → Completed</c> and attach the
    /// result JSON. Staged only — committed by the caller's SaveChanges #2, atomically with the
    /// ledger posting and aggregate.
    /// </summary>
    Task CompleteAsync(
        string tenantId, string idempotencyKey, string resultJson, CancellationToken cancellationToken = default);
}
