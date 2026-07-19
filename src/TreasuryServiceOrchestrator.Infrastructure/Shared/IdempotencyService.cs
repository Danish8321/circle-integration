using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.Infrastructure.Shared;

public sealed class IdempotencyService(TreasuryServiceOrchestratorDbContext dbContext, TimeProvider timeProvider)
    : IIdempotencyService
{
    public async Task<IdempotencyOutcome> TryBeginAsync(
        string tenantId, string idempotencyKey, string requestHash, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.IdempotencyRecords.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is null)
        {
            await dbContext.IdempotencyRecords.AddAsync(
                new IdempotencyRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    IdempotencyKey = idempotencyKey,
                    RequestHash = requestHash,
                    Status = IdempotencyStatus.InProgress,
                    ResultJson = null,
                    CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                    CompletedAtUtc = null,
                },
                cancellationToken);

            return new IdempotencyOutcome.Started();
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Idempotency key '{idempotencyKey}' was already used with a different request payload.");
        }

        return existing.Status == IdempotencyStatus.Completed
            ? new IdempotencyOutcome.Replay(
                existing.ResultJson
                ?? throw new InvalidOperationException("Completed idempotency record has a null result."))
            : new IdempotencyOutcome.InFlightRetry();
    }

    public async Task CompleteAsync(
        string tenantId, string idempotencyKey, string resultJson, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.IdempotencyRecords.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No idempotency record for key '{idempotencyKey}' to complete.");

        existing.Status = IdempotencyStatus.Completed;
        existing.ResultJson = resultJson;
        existing.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
    }
}
