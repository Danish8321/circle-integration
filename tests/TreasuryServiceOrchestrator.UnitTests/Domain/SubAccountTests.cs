using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class SubAccountTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidClientCompanyId_StartsInCreatedState()
    {
        var subAccount = SubAccount.Create("client-1", NowUtc);

        Assert.Equal(SubAccountLifecycleState.Created, subAccount.LifecycleState);
        Assert.Equal("client-1", subAccount.ClientCompanyId);
        Assert.False(subAccount.IsDisabled);
        Assert.Null(subAccount.CircleWalletId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankClientCompanyId_Throws(string clientCompanyId)
    {
        Assert.Throws<ArgumentException>(() => SubAccount.Create(clientCompanyId, NowUtc));
    }

    [Fact]
    public void BeginCompliance_FromCreated_TransitionsToPendingComplianceWithWalletId()
    {
        var subAccount = SubAccount.Create("client-1", NowUtc);

        subAccount.BeginCompliance("wallet-123");

        Assert.Equal(SubAccountLifecycleState.PendingCompliance, subAccount.LifecycleState);
        Assert.Equal("wallet-123", subAccount.CircleWalletId);
    }

    [Fact]
    public void BeginCompliance_CalledTwice_ThrowsOnSecondCall()
    {
        var subAccount = SubAccount.Create("client-1", NowUtc);
        subAccount.BeginCompliance("wallet-123");

        Assert.Throws<InvalidOperationException>(() => subAccount.BeginCompliance("wallet-456"));
    }

    [Fact]
    public void BeginCompliance_WithBlankWalletId_Throws()
    {
        var subAccount = SubAccount.Create("client-1", NowUtc);

        Assert.Throws<ArgumentException>(() => subAccount.BeginCompliance(" "));
    }
}
