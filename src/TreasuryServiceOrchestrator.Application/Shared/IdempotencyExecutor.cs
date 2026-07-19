using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.Application.Shared;

/// <summary>
/// Runs a mutating use case under the reserve → gateway → complete flow (CLAUDE.md invariant 11)
/// with a persisted idempotency reservation:
/// <list type="number">
/// <item><b>reserve</b> — <see cref="IIdempotencyService.TryBeginAsync"/> stages an
/// <c>InProgress</c> record; SaveChanges #1 persists it <i>before</i> the gateway call.</item>
/// <item><b>work</b> — the caller's delegate calls the provider and stages the ledger posting and
/// aggregate without committing.</item>
/// <item><b>complete</b> — <see cref="IIdempotencyService.CompleteAsync"/> flips the record to
/// <c>Completed</c> with the result; SaveChanges #2 commits it together with the staged work,
/// atomically.</item>
/// </list>
/// A replay returns the cached result. An in-flight retry (a prior attempt reserved but never
/// completed — e.g. a crash after the gateway call) re-drives the work: the provider dedups on the
/// forwarded idempotency key, and any prior ledger insert was rolled back with the missing
/// SaveChanges #2, so the re-drive posts cleanly.
/// </summary>
public static class IdempotencyExecutor
{
    public static async Task<TResult> ExecuteAsync<TResult>(
        IIdempotencyService idempotency,
        string tenantId,
        string idempotencyKey,
        object requestPayload,
        IUnitOfWork unitOfWork,
        Func<Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        var requestHash = HashPayload(requestPayload);

        var outcome = await idempotency.TryBeginAsync(
            tenantId, idempotencyKey, requestHash, cancellationToken);

        switch (outcome)
        {
            case IdempotencyOutcome.Replay replay:
                return JsonSerializer.Deserialize<TResult>(replay.ResultJson)
                    ?? throw new InvalidOperationException("Cached idempotency result deserialized to null.");

            case IdempotencyOutcome.Started:
                // SaveChanges #1 — persist the reservation before the provider is called.
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;

            case IdempotencyOutcome.InFlightRetry:
                // Reservation already persisted by a prior attempt; re-drive to finish it.
                break;
        }

        var result = await work();

        await idempotency.CompleteAsync(
            tenantId, idempotencyKey, JsonSerializer.Serialize(result), cancellationToken);

        // SaveChanges #2 — commit the completion together with the work staged by the delegate.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    private static string HashPayload(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
