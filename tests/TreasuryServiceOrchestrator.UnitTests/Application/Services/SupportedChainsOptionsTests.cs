
namespace TreasuryServiceOrchestrator.UnitTests.Application.Services;

public sealed class SupportedChainsOptionsTests
{
    [Fact]
    public void IsSupported_WithConfiguredChain_ReturnsTrue()
    {
        var options = new SupportedChainsOptions { Chains = ["ETH", "MATIC"] };

        Assert.True(options.IsSupported("MATIC"));
    }

    [Fact]
    public void IsSupported_WithUnconfiguredChain_ReturnsFalse()
    {
        var options = new SupportedChainsOptions { Chains = ["ETH"] };

        Assert.False(options.IsSupported("SOL"));
    }

    [Fact]
    public void DefaultChains_ContainsEth()
    {
        var options = new SupportedChainsOptions();

        Assert.True(options.IsSupported("ETH"));
    }
}
