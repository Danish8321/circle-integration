using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Reconciliation;

public sealed class DepositReconciliationServiceTests
{
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IStablecoinGateway> stablecoinGateway = new();
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly Mock<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>> processDepositHandler = new();
    private readonly Mock<ISettableCallerContext> callerContext = new();
    private readonly ReconciliationOptions options = new();
    private readonly DepositReconciliationService service;

    public DepositReconciliationServiceTests()
    {
        service = new DepositReconciliationService(
            subAccounts.Object,
            stablecoinGateway.Object,
            transactions.Object,
            processDepositHandler.Object,
            callerContext.Object,
            TimeProvider.System,
            options,
            NullLogger<DepositReconciliationService>.Instance);

        transactions
            .Setup(x => x.GetByProviderReferenceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null);
        processDepositHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessDepositCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDepositResult(
                Guid.NewGuid(), Guid.NewGuid(), new Money(1m, "USDC"), TransactionStatus.Complete, DateTime.UtcNow));
    }

    private static SubAccount ActiveWithWallet(string clientCompanyId = "client-1", string walletId = "wallet-1")
    {
        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        subAccount.UpdateCircleWalletId(walletId);
        return subAccount;
    }

    private static ProviderDepositRecord Deposit(string providerReferenceId, string walletId = "wallet-1") => new(
        providerReferenceId, walletId, "0xabc", new Money(50m, "USDC"), DepositSourceType.Wire, DateTime.UtcNow);

    [Fact]
    public async Task RunOnceAsync_WithUnrecordedProviderDeposit_CreditsItViaProcessDepositHandlerWithReconciliationCorrelationId()
    {
        var subAccount = ActiveWithWallet();
        subAccounts.Setup(x => x.ListActiveWithWalletAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([subAccount]);
        var deposit = Deposit("provider-ref-1");
        stablecoinGateway
            .Setup(x => x.ListRecentDepositsAsync("wallet-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([deposit]);

        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        healedCount.Should().Be(1);
        processDepositHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessDepositCommand>(c =>
                    c.SubAccountId == subAccount.Id &&
                    c.ProviderReferenceId == "provider-ref-1" &&
                    c.CorrelationId == "reconciliation-provider-ref-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        callerContext.Verify(x => x.Set("client-1", CallerRole.SubAccount), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_WithAlreadyRecordedProviderDeposit_SkipsWithoutDoubleCrediting()
    {
        var subAccount = ActiveWithWallet();
        subAccounts.Setup(x => x.ListActiveWithWalletAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([subAccount]);
        var deposit = Deposit("provider-ref-1");
        stablecoinGateway
            .Setup(x => x.ListRecentDepositsAsync("wallet-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([deposit]);
        transactions
            .Setup(x => x.GetByProviderReferenceIdAsync("provider-ref-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transaction.Create(
                subAccount.Id, "client-1", TransactionType.Deposit, TransactionStatus.Complete,
                new Money(50m, "USDC"), "provider-ref-1", DepositSourceType.Wire, null, "corr-1", DateTime.UtcNow));

        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        healedCount.Should().Be(0);
        processDepositHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessDepositCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_WithOneWalletGatewayFailure_StillReconcilesRemainingWallets()
    {
        var failingSubAccount = ActiveWithWallet("client-1", "wallet-fail");
        var healthySubAccount = ActiveWithWallet("client-2", "wallet-ok");
        subAccounts.Setup(x => x.ListActiveWithWalletAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([failingSubAccount, healthySubAccount]);
        stablecoinGateway
            .Setup(x => x.ListRecentDepositsAsync("wallet-fail", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider outage"));
        var deposit = Deposit("provider-ref-2", "wallet-ok");
        stablecoinGateway
            .Setup(x => x.ListRecentDepositsAsync("wallet-ok", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([deposit]);

        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        healedCount.Should().Be(1);
        processDepositHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessDepositCommand>(c => c.ProviderReferenceId == "provider-ref-2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_WithOneDepositProcessingFailure_StillProcessesRemainingDepositsInSameWallet()
    {
        var subAccount = ActiveWithWallet();
        subAccounts.Setup(x => x.ListActiveWithWalletAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([subAccount]);
        var failingDeposit = Deposit("provider-ref-fail");
        var okDeposit = Deposit("provider-ref-ok");
        stablecoinGateway
            .Setup(x => x.ListRecentDepositsAsync("wallet-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([failingDeposit, okDeposit]);
        processDepositHandler
            .Setup(x => x.HandleAsync(
                It.Is<ProcessDepositCommand>(c => c.ProviderReferenceId == "provider-ref-fail"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sub-account rejected credit"));

        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        healedCount.Should().Be(1);
        processDepositHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessDepositCommand>(c => c.ProviderReferenceId == "provider-ref-ok"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_WithNoActiveWalletedSubAccounts_NeverQueriesTheGateway()
    {
        subAccounts.Setup(x => x.ListActiveWithWalletAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);

        healedCount.Should().Be(0);
        stablecoinGateway.Verify(
            x => x.ListRecentDepositsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
