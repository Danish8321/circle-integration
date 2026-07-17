namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// <see cref="IMockRandomSource"/> backed by <see cref="Random.Shared"/>, which is thread-safe
/// since .NET 6 — required because mock gateways are singletons serving concurrent requests and
/// must never use <c>new Random()</c>.
/// </summary>
public sealed class SystemRandomSource : IMockRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();

    public Guid NewGuid() => Guid.NewGuid();
}
