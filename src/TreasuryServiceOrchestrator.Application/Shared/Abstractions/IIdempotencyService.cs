namespace TreasuryServiceOrchestrator.Application.Shared.Abstractions;

public interface IIdempotencyService
{
    Task<string?> TryGetCachedResultJsonAsync(
        string tenantId, string idempotencyKey, string requestHash, CancellationToken cancellationToken = default);

    Task StoreResultAsync(
        string tenantId, string idempotencyKey, string requestHash, string resultJson,
        CancellationToken cancellationToken = default);
}
