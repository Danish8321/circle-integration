namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

/// <summary>
/// Minimal manually-controlled <see cref="TimeProvider"/> for tests. No
/// <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c> package is referenced anywhere in
/// the repo, so this is constructed by hand rather than adding a new dependency.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;
}
