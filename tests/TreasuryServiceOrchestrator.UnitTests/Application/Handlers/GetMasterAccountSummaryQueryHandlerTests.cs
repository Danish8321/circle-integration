using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class GetMasterAccountSummaryQueryHandlerTests
{
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IBalanceSnapshotRepository> snapshots = new();
    private readonly Mock<IStablecoinGateway> gateway = new();
    private readonly GetMasterAccountSummaryQueryHandler handler;

    public GetMasterAccountSummaryQueryHandlerTests()
    {
        handler = new GetMasterAccountSummaryQueryHandler(
            subAccounts.Object, snapshots.Object, gateway.Object);
    }

    private static SubAccount ProvisionedSubAccount(string clientCompanyId) =>
        SubAccount.Create(clientCompanyId, new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task HandleAsync_SumsLatestSnapshotsPlusMainWallet_AndReportsSubAccountCount()
    {
        var first = ProvisionedSubAccount("client-1");
        var second = ProvisionedSubAccount("client-2");
        subAccounts
            .Setup(x => x.ListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        snapshots
            .Setup(x => x.GetLatestAsync(first.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BalanceSnapshot.Create(
                first.Id, "client-1", new Money(500m, "USDC"), BalanceSnapshotReason.PostMutation,
                new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc)));
        snapshots
            .Setup(x => x.GetLatestAsync(second.Id, "client-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BalanceSnapshot.Create(
                second.Id, "client-2", new Money(250m, "USDC"), BalanceSnapshotReason.PostMutation,
                new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc)));
        gateway
            .Setup(x => x.GetMainWalletBalanceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Money(10_000m, "USDC"));

        var result = await handler.HandleAsync(new GetMasterAccountSummaryQuery(), TestContext.Current.CancellationToken);

        result.SubAccountCount.Should().Be(2);
        result.TotalSubAccountBalance.Amount.Should().Be(750m);
        result.MainWalletBalance.Amount.Should().Be(10_000m);
    }

    [Fact]
    public async Task HandleAsync_SubAccountWithNoSnapshot_ContributesZero()
    {
        var withSnapshot = ProvisionedSubAccount("client-1");
        var withoutSnapshot = ProvisionedSubAccount("client-2");
        subAccounts
            .Setup(x => x.ListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([withSnapshot, withoutSnapshot]);
        snapshots
            .Setup(x => x.GetLatestAsync(withSnapshot.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BalanceSnapshot.Create(
                withSnapshot.Id, "client-1", new Money(500m, "USDC"), BalanceSnapshotReason.PostMutation,
                new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc)));
        snapshots
            .Setup(x => x.GetLatestAsync(withoutSnapshot.Id, "client-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BalanceSnapshot?)null);
        gateway
            .Setup(x => x.GetMainWalletBalanceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Money(0m, "USDC"));

        var result = await handler.HandleAsync(new GetMasterAccountSummaryQuery(), TestContext.Current.CancellationToken);

        result.SubAccountCount.Should().Be(2);
        result.TotalSubAccountBalance.Amount.Should().Be(500m);
    }
}
