using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Snapshots;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Snapshots;

public sealed class ScheduledBalanceSnapshotServiceTests
{
    private readonly Mock<IFundAccountRepository> fundAccounts = new();
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IBalanceSnapshotRepository> balanceSnapshots = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly ScheduledBalanceSnapshotService service;

    public ScheduledBalanceSnapshotServiceTests()
    {
        service = new ScheduledBalanceSnapshotService(
            fundAccounts.Object,
            subAccounts.Object,
            balanceSnapshots.Object,
            unitOfWork.Object,
            TimeProvider.System);
    }

    [Fact]
    public async Task RunOnceAsync_WritesOneScheduledSnapshotPerFundAccount()
    {
        var fundAccount1 = FundAccount.Create("client-1", new Money(10m, "USDC"), DateTime.UtcNow);
        var fundAccount2 = FundAccount.Create("client-2", new Money(20m, "USDC"), DateTime.UtcNow);
        var subAccount1 = SubAccount.Create("client-1", DateTime.UtcNow);
        var subAccount2 = SubAccount.Create("client-2", DateTime.UtcNow);

        fundAccounts
            .Setup(x => x.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundAccount1, fundAccount2]);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount1);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount2);

        var result = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        result.Should().Be(2);
        balanceSnapshots.Verify(
            x => x.AddAsync(
                It.Is<BalanceSnapshot>(s =>
                    s.SubAccountId == subAccount1.Id &&
                    s.ClientCompanyId == "client-1" &&
                    s.Balance == new Money(10m, "USDC") &&
                    s.Reason == BalanceSnapshotReason.Scheduled),
                It.IsAny<CancellationToken>()),
            Times.Once);
        balanceSnapshots.Verify(
            x => x.AddAsync(
                It.Is<BalanceSnapshot>(s =>
                    s.SubAccountId == subAccount2.Id &&
                    s.ClientCompanyId == "client-2" &&
                    s.Balance == new Money(20m, "USDC") &&
                    s.Reason == BalanceSnapshotReason.Scheduled),
                It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunOnceAsync_WhenOneAccountFails_StillWritesSnapshotsForTheRest()
    {
        var fundAccount1 = FundAccount.Create("client-1", new Money(10m, "USDC"), DateTime.UtcNow);
        var fundAccount2 = FundAccount.Create("client-2", new Money(20m, "USDC"), DateTime.UtcNow);
        var subAccount2 = SubAccount.Create("client-2", DateTime.UtcNow);

        fundAccounts
            .Setup(x => x.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundAccount1, fundAccount2]);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount2);

        var result = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        result.Should().Be(1);
        balanceSnapshots.Verify(
            x => x.AddAsync(
                It.Is<BalanceSnapshot>(s => s.ClientCompanyId == "client-2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        balanceSnapshots.Verify(
            x => x.AddAsync(
                It.Is<BalanceSnapshot>(s => s.ClientCompanyId == "client-1"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_WhenSubAccountMissing_SkipsWithoutAbortingRest()
    {
        var fundAccount1 = FundAccount.Create("client-1", new Money(10m, "USDC"), DateTime.UtcNow);
        var fundAccount2 = FundAccount.Create("client-2", new Money(20m, "USDC"), DateTime.UtcNow);
        var subAccount2 = SubAccount.Create("client-2", DateTime.UtcNow);

        fundAccounts
            .Setup(x => x.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundAccount1, fundAccount2]);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount2);

        var result = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        result.Should().Be(1);
    }
}
