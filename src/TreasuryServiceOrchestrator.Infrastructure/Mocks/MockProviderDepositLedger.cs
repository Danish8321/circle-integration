using System.Collections.Concurrent;

using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// In-memory singleton backing <see cref="IMockProviderDepositLedger"/> — thread-safe since mock
/// gateways are registered as singletons (docs/features/05-reliability-and-error-handling.md
/// §7.7, §7.3).
/// </summary>
public sealed class MockProviderDepositLedger : IMockProviderDepositLedger
{
    private readonly ConcurrentBag<ProviderDepositRecord> records = [];

    public Task SeedAsync(ProviderDepositRecord record, CancellationToken ct = default)
    {
        records.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderDepositRecord>> ListAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken ct = default)
    {
        IReadOnlyList<ProviderDepositRecord> result = records
            .Where(x => string.Equals(x.CircleWalletId, circleWalletId, StringComparison.Ordinal) && x.OccurredAtUtc >= sinceUtc)
            .ToList();

        return Task.FromResult(result);
    }
}
