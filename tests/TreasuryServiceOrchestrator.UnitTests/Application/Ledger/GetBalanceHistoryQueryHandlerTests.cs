using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger;

public sealed class GetBalanceHistoryQueryHandlerTests
{
    private readonly Mock<IBalanceSnapshotRepository> balanceSnapshots = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetBalanceHistoryQueryHandler handler;

    public GetBalanceHistoryQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetBalanceHistoryQueryHandler(balanceSnapshots.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithSnapshotsForSubAccount_ReturnsMappedResults()
    {
        var subAccountId = Guid.NewGuid();
        var snapshot = BalanceSnapshot.Create(
            subAccountId, "client-1", new Money(15m, "USDC"), BalanceSnapshotReason.PostMutation, DateTime.UtcNow);
        balanceSnapshots
            .Setup(x => x.ListBySubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([snapshot]);

        var results = await handler.HandleAsync(
            new GetBalanceHistoryQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        results[0].BalanceSnapshotId.Should().Be(snapshot.Id);
        results[0].Balance.Should().Be(new Money(15m, "USDC"));
        results[0].Reason.Should().Be(BalanceSnapshotReason.PostMutation);
    }

    [Fact]
    public async Task HandleAsync_WithUnidentifiedCaller_ThrowsTenantForbiddenWithoutQueryingRepository()
    {
        callerContext.Setup(x => x.CallerId).Returns(string.Empty);

        var act = () => handler.HandleAsync(
            new GetBalanceHistoryQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        balanceSnapshots.Verify(
            x => x.ListBySubAccountAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNoSnapshots_ReturnsEmptyList()
    {
        var subAccountId = Guid.NewGuid();
        balanceSnapshots
            .Setup(x => x.ListBySubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            new GetBalanceHistoryQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }
}
