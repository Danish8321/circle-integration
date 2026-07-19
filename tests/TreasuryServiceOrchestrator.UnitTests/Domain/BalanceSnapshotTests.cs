
namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class BalanceSnapshotTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SubAccountId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidFields_SetsAllProperties()
    {
        var balance = new Money(250m, "USDC");

        var snapshot = BalanceSnapshot.Create(
            SubAccountId,
            "client-1",
            balance,
            BalanceSnapshotReason.PostMutation,
            NowUtc);

        Assert.NotEqual(Guid.Empty, snapshot.Id);
        Assert.Equal(SubAccountId, snapshot.SubAccountId);
        Assert.Equal("client-1", snapshot.ClientCompanyId);
        Assert.Equal(balance, snapshot.Balance);
        Assert.Equal(BalanceSnapshotReason.PostMutation, snapshot.Reason);
        Assert.Equal(NowUtc, snapshot.CapturedAtUtc);
    }

    [Fact]
    public void Create_WithScheduledReason_SetsReason()
    {
        var snapshot = BalanceSnapshot.Create(
            SubAccountId,
            "client-1",
            new Money(0m, "USDC"),
            BalanceSnapshotReason.Scheduled,
            NowUtc);

        Assert.Equal(BalanceSnapshotReason.Scheduled, snapshot.Reason);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => BalanceSnapshot.Create(
                Guid.Empty,
                "client-1",
                new Money(1m, "USDC"),
                BalanceSnapshotReason.PostMutation,
                NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankClientCompanyId_Throws(string clientCompanyId)
    {
        Assert.Throws<ArgumentException>(
            () => BalanceSnapshot.Create(
                SubAccountId,
                clientCompanyId,
                new Money(1m, "USDC"),
                BalanceSnapshotReason.PostMutation,
                NowUtc));
    }

    [Fact]
    public void Create_WithNullBalance_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => BalanceSnapshot.Create(
                SubAccountId,
                "client-1",
                null!,
                BalanceSnapshotReason.PostMutation,
                NowUtc));
    }
}
