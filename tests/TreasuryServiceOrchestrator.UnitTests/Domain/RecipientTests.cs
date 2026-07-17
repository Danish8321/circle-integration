using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class RecipientTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SubAccountId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidFields_SetsAllProperties()
    {
        var recipient = Recipient.Create(
            SubAccountId,
            "client-1",
            "ETH",
            "0xabc123",
            "My Recipient",
            "circle-recipient-1",
            RecipientStatus.PendingApproval,
            NowUtc);

        Assert.NotEqual(Guid.Empty, recipient.Id);
        Assert.Equal(SubAccountId, recipient.SubAccountId);
        Assert.Equal("client-1", recipient.ClientCompanyId);
        Assert.Equal("ETH", recipient.Chain);
        Assert.Equal("0xabc123", recipient.Address);
        Assert.Equal("My Recipient", recipient.Label);
        Assert.Equal("circle-recipient-1", recipient.CircleRecipientId);
        Assert.Equal(RecipientStatus.PendingApproval, recipient.Status);
        Assert.Null(recipient.DenialReason);
        Assert.Equal(NowUtc, recipient.CreatedAtUtc);
        Assert.Equal(NowUtc, recipient.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithNullCircleRecipientId_AllowsNull()
    {
        var recipient = Recipient.Create(
            SubAccountId,
            "client-1",
            "ETH",
            "0xabc123",
            "My Recipient",
            null,
            RecipientStatus.PendingApproval,
            NowUtc);

        Assert.Null(recipient.CircleRecipientId);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Recipient.Create(
                Guid.Empty, "client-1", "ETH", "0xabc123", "My Recipient", null, RecipientStatus.PendingApproval, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankClientCompanyId_Throws(string clientCompanyId)
    {
        Assert.Throws<ArgumentException>(
            () => Recipient.Create(
                SubAccountId, clientCompanyId, "ETH", "0xabc123", "My Recipient", null, RecipientStatus.PendingApproval, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankChain_Throws(string chain)
    {
        Assert.Throws<ArgumentException>(
            () => Recipient.Create(
                SubAccountId, "client-1", chain, "0xabc123", "My Recipient", null, RecipientStatus.PendingApproval, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankAddress_Throws(string address)
    {
        Assert.Throws<ArgumentException>(
            () => Recipient.Create(
                SubAccountId, "client-1", "ETH", address, "My Recipient", null, RecipientStatus.PendingApproval, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankLabel_Throws(string label)
    {
        Assert.Throws<ArgumentException>(
            () => Recipient.Create(
                SubAccountId, "client-1", "ETH", "0xabc123", label, null, RecipientStatus.PendingApproval, NowUtc));
    }
}
