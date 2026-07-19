
namespace TreasuryServiceOrchestrator.TestUtilities;

/// <summary>
/// Deterministic <see cref="IMockRandomSource"/> test double. <see cref="NextDouble"/> always
/// returns the configured fixed value. <see cref="NewGuid"/> returns the configured fixed
/// <see cref="Guid"/> by default, or increments through a deterministic sequence derived from it
/// when <paramref name="incrementGuidOnEachCall"/> is <c>true</c> — useful for tests that need to
/// assert distinct ids were generated across multiple calls while remaining fully deterministic.
/// </summary>
public sealed class FixedRandomSource(
    double fixedDouble = 0.0,
    Guid? fixedGuid = null,
    bool incrementGuidOnEachCall = false) : IMockRandomSource
{
    private static readonly Guid DefaultFixedGuid = new("00000000-0000-0000-0000-000000000001");

    private readonly Guid _fixedGuid = fixedGuid ?? DefaultFixedGuid;
    private int _guidCallCount;

    public double NextDouble() => fixedDouble;

    public Guid NewGuid()
    {
        if (!incrementGuidOnEachCall)
        {
            return _fixedGuid;
        }

        var bytes = _fixedGuid.ToByteArray();
        var counter = _guidCallCount++;
        bytes[15] = unchecked((byte)(bytes[15] + counter));
        return new Guid(bytes);
    }
}
