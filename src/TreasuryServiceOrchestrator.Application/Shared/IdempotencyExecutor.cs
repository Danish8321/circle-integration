using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.Application.Shared;

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

        var cachedJson = await idempotency.TryGetCachedResultJsonAsync(
            tenantId, idempotencyKey, requestHash, cancellationToken);
        if (cachedJson is not null)
        {
            return JsonSerializer.Deserialize<TResult>(cachedJson)
                ?? throw new InvalidOperationException("Cached idempotency result deserialized to null.");
        }

        var result = await work();

        await idempotency.StoreResultAsync(
            tenantId, idempotencyKey, requestHash, JsonSerializer.Serialize(result), cancellationToken);
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
