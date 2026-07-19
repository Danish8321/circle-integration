using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class DepositAddressTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SubAccountId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidFields_SetsAllProperties()
    {
        var depositAddress = DepositAddress.Create(
            SubAccountId,
            "ETH",
            "USDC",
            "0xabc123",
            "circle-address-1",
            NowUtc);

        Assert.NotEqual(Guid.Empty, depositAddress.Id);
        Assert.Equal(SubAccountId, depositAddress.SubAccountId);
        Assert.Equal("ETH", depositAddress.Chain);
        Assert.Equal("USDC", depositAddress.Currency);
        Assert.Equal("0xabc123", depositAddress.Address);
        Assert.Equal("circle-address-1", depositAddress.CircleAddressId);
        Assert.Equal(NowUtc, depositAddress.CreatedAtUtc);
    }

    [Fact]
    public void Create_WithNullCircleAddressId_AllowsNull()
    {
        var depositAddress = DepositAddress.Create(
            SubAccountId,
            "ETH",
            "USDC",
            "0xabc123",
            null,
            NowUtc);

        Assert.Null(depositAddress.CircleAddressId);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => DepositAddress.Create(Guid.Empty, "ETH", "USDC", "0xabc123", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankChain_Throws(string chain)
    {
        Assert.Throws<ArgumentException>(
            () => DepositAddress.Create(SubAccountId, chain, "USDC", "0xabc123", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankCurrency_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(
            () => DepositAddress.Create(SubAccountId, "ETH", currency, "0xabc123", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankAddress_Throws(string address)
    {
        Assert.Throws<ArgumentException>(
            () => DepositAddress.Create(SubAccountId, "ETH", "USDC", address, null, NowUtc));
    }
}
