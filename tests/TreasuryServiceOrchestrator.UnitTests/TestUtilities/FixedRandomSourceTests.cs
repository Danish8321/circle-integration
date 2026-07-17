using FluentAssertions;

using TreasuryServiceOrchestrator.TestUtilities;

namespace TreasuryServiceOrchestrator.UnitTests.TestUtilities;

public class FixedRandomSourceTests
{
    [Fact]
    public void NextDouble_ReturnsConfiguredFixedValue()
    {
        var source = new FixedRandomSource(fixedDouble: 0.42);

        source.NextDouble().Should().Be(0.42);
        source.NextDouble().Should().Be(0.42);
    }

    [Fact]
    public void NewGuid_ReturnsConfiguredFixedGuid_ByDefault()
    {
        var fixedGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var source = new FixedRandomSource(fixedGuid: fixedGuid);

        source.NewGuid().Should().Be(fixedGuid);
        source.NewGuid().Should().Be(fixedGuid);
    }

    [Fact]
    public void NewGuid_UsesDefaultFixedGuid_WhenNoneConfigured()
    {
        var source = new FixedRandomSource();

        source.NewGuid().Should().NotBe(Guid.Empty);
        source.NewGuid().Should().Be(source.NewGuid());
    }

    [Fact]
    public void NewGuid_IncrementsSequence_WhenConfigured()
    {
        var fixedGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var source = new FixedRandomSource(fixedGuid: fixedGuid, incrementGuidOnEachCall: true);

        var first = source.NewGuid();
        var second = source.NewGuid();

        first.Should().NotBe(second);
    }
}
