using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Webhooks;

/// <summary>
/// Ticket 04.6: <see cref="DepositsWebhookTopicProcessor"/> parses Circle's real SNS-unwrapped
/// "deposits" envelope (fiat-wire only, docs/features/09-deposits-and-funding.md §3.5) and
/// dispatches into <see cref="ProcessDepositCommandHandler"/> via
/// <see cref="ICommandHandler{TCommand,TResult}"/>.
/// </summary>
public sealed class DepositsWebhookTopicProcessorTests
{
    private readonly Mock<ISubAccountRepository> subAccountRepository = new();
    private readonly Mock<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>> processDepositHandler = new();
    private readonly Mock<ISettableCallerContext> callerContext = new();

    private DepositsWebhookTopicProcessor CreateSut() =>
        new(subAccountRepository.Object, processDepositHandler.Object, callerContext.Object);

    private static SubAccount ActiveSubAccount(string circleWalletId)
    {
        var subAccount = SubAccount.Create("client-1", DateTime.UtcNow);
        subAccount.BeginCompliance(circleWalletId);
        subAccount.MarkAccepted();
        return subAccount;
    }

    [Fact]
    public void Topic_IsDeposits()
    {
        CreateSut().Topic.Should().Be("deposits");
    }

    [Fact]
    public async Task ProcessAsync_ParsesEnvelope_ResolvesSubAccountByWalletId_AndDispatchesWireDeposit()
    {
        var subAccount = ActiveSubAccount("wallet-123");
        subAccountRepository
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);
        processDepositHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessDepositCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDepositResult(
                Guid.NewGuid(), subAccount.Id, new Money(100m, "USD"), TransactionStatus.Complete, DateTime.UtcNow));

        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "deposits",
              "version": 2,
              "deposit": {
                "id": "deposit-abc",
                "walletId": "wallet-123",
                "amount": { "amount": "100.00", "currency": "USD" }
              }
            }
            """;

        await CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        callerContext.Verify(x => x.Set("client-1", CallerRole.SubAccount), Times.Once);
        processDepositHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessDepositCommand>(cmd =>
                    cmd.SubAccountId == subAccount.Id
                    && cmd.ProviderReferenceId == "deposit-abc"
                    && cmd.DepositSourceType == DepositSourceType.Wire
                    && cmd.Amount.Amount == 100.00m
                    && cmd.Amount.CurrencyCode == "USD"
                    && cmd.CorrelationId == "deposit-abc"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenWalletIdDoesNotResolveToASubAccount_ThrowsDepositSourceNotResolvedException()
    {
        subAccountRepository
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);

        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "deposits",
              "version": 2,
              "deposit": {
                "id": "deposit-abc",
                "walletId": "wallet-unknown",
                "amount": { "amount": "100.00", "currency": "USD" }
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DepositSourceNotResolvedException>();
        processDepositHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessDepositCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenPayloadHasNoWalletId_ThrowsDepositSourceNotResolvedException()
    {
        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "deposits",
              "version": 2,
              "deposit": {
                "id": "deposit-abc",
                "walletId": "",
                "amount": { "amount": "100.00", "currency": "USD" }
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DepositSourceNotResolvedException>();
        subAccountRepository.Verify(
            x => x.GetByCircleWalletIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
