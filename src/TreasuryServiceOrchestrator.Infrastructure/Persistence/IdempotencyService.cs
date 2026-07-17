using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class IdempotencyService(TreasuryServiceOrchestratorDbContext dbContext, TimeProvider timeProvider)
    : IIdempotencyService
{
    public async Task<string?> TryGetCachedResultJsonAsync(
        string tenantId, string idempotencyKey, string requestHash, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.IdempotencyRecords.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Idempotency key '{idempotencyKey}' was already used with a different request payload.");
        }

        return existing.ResultJson;
    }

    public async Task StoreResultAsync(
        string tenantId, string idempotencyKey, string requestHash, string resultJson,
        CancellationToken cancellationToken = default)
    {
        await dbContext.IdempotencyRecords.AddAsync(
            new IdempotencyRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash,
                ResultJson = resultJson,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            },
            cancellationToken);
    }
}
