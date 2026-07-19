using FluentAssertions;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public sealed class SystemRandomSourceTests
{
    [Fact]
    public void NextDouble_ReturnsValueInZeroToOneRange()
    {
        var sut = new SystemRandomSource();

        var value = sut.NextDouble();

        value.Should().BeGreaterThanOrEqualTo(0.0);
        value.Should().BeLessThan(1.0);
    }

    [Fact]
    public void NewGuid_ReturnsNonEmptyGuid()
    {
        var sut = new SystemRandomSource();

        var value = sut.NewGuid();

        value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void NewGuid_ReturnsDifferentValuesAcrossCalls()
    {
        var sut = new SystemRandomSource();

        var first = sut.NewGuid();
        var second = sut.NewGuid();

        first.Should().NotBe(second);
    }
}
