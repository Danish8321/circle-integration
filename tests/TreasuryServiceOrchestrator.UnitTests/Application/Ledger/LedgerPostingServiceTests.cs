using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger;

public sealed class LedgerPostingServiceTests
{
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly Mock<IBalanceSnapshotRepository> balanceSnapshots = new();
    private readonly Mock<IFundAccountRepository> fundAccounts = new();
    private readonly Mock<INotificationOutboxRepository> outbox = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly LedgerPostingService service;

    public LedgerPostingServiceTests()
    {
        service = new LedgerPostingService(
            transactions.Object,
            balanceSnapshots.Object,
            fundAccounts.Object,
            outbox.Object,
            unitOfWork.Object,
            TimeProvider.System);
    }

    private static LedgerPosting Posting(decimal amount, string clientCompanyId = "client-1") => new(
        SubAccountId: Guid.NewGuid(),
        ClientCompanyId: clientCompanyId,
        Type: TransactionType.Deposit,
        Amount: new Money(amount, "USDC"),
        ProviderReferenceId: "provider-ref-1",
        DepositSourceType: DepositSourceType.Wire,
        CorrelationId: "correlation-1");

    [Fact]
    public async Task PostAsync_WithCreditOnExistingAccount_IncreasesBalance()
    {
        var existing = FundAccount.Create("client-1", new Money(100m, "USDC"), DateTime.UtcNow);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var posting = Posting(50m);

        await service.PostAsync(posting, outboxEntryBuilder: null, ct: TestContext.Current.CancellationToken);

        existing.Balance.Amount.Should().Be(150m);
        fundAccounts.Verify(
            x => x.AddAsync(It.IsAny<FundAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostAsync_WithDebitOnExistingAccount_DecreasesBalance()
    {
        var existing = FundAccount.Create("client-1", new Money(100m, "USDC"), DateTime.UtcNow);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var posting = Posting(-30m);

        await service.PostAsync(posting, outboxEntryBuilder: null, ct: TestContext.Current.CancellationToken);

        existing.Balance.Amount.Should().Be(70m);
    }

    [Fact]
    public async Task PostAsync_WithNoExistingAccount_CreatesFundAccountFromZero()
    {
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundAccount?)null);
        var posting = Posting(25m);

        await service.PostAsync(posting, outboxEntryBuilder: null, ct: TestContext.Current.CancellationToken);

        fundAccounts.Verify(
            x => x.AddAsync(
                It.Is<FundAccount>(a => a.Balance.Amount == 25m && a.ClientCompanyId == "client-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostAsync_PersistsTransactionWithSignedAmount()
    {
        var existing = FundAccount.Create("client-1", Money.Zero("USDC"), DateTime.UtcNow);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var posting = Posting(-15m);

        var result = await service.PostAsync(posting, outboxEntryBuilder: null, ct: TestContext.Current.CancellationToken);

        result.Amount.Amount.Should().Be(-15m);
        result.SubAccountId.Should().Be(posting.SubAccountId);
        result.Status.Should().Be(TransactionStatus.Complete);
        transactions.Verify(
            x => x.AddAsync(
                It.Is<Transaction>(t => t.Amount.Amount == -15m), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostAsync_WritesBalanceSnapshotWithPostMutationReasonAndResultingBalance()
    {
        var existing = FundAccount.Create("client-1", new Money(10m, "USDC"), DateTime.UtcNow);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var posting = Posting(5m);

        await service.PostAsync(posting, outboxEntryBuilder: null, ct: TestContext.Current.CancellationToken);

        balanceSnapshots.Verify(
            x => x.AddAsync(
                It.Is<BalanceSnapshot>(s =>
                    s.Reason == BalanceSnapshotReason.PostMutation
                    && s.Balance.Amount == 15m
                    && s.SubAccountId == posting.SubAccountId
                    && s.ClientCompanyId == "client-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostAsync_SavesChangesOnce()
    {
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FundAccount.Create("client-1", Money.Zero("USDC"), DateTime.UtcNow));
        var posting = Posting(10m);

        await service.PostAsync(posting, outboxEntryBuilder: null, ct: TestContext.Current.CancellationToken);

        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
