using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Webhooks;

/// <summary>
/// Ticket 06.4: <see cref="TransfersWebhookTopicProcessor"/> is dual-direction (doc-grilling
/// correction #5, docs/features/10-outbound-transfers-and-recipients.md §3.5) — an outgoing
/// branch (a local <see cref="Transfer"/> row already exists) dispatches
/// <see cref="ProcessTransferStatusCommand"/>, and an incoming branch (no local row) dispatches
/// <see cref="ProcessDepositCommand"/> after resolving the owning SubAccount from the payload's
/// destination wallet id.
/// </summary>
public sealed class TransfersWebhookTopicProcessorTests
{
    private readonly Mock<ITransferRepository> transferRepository = new();
    private readonly Mock<ISubAccountRepository> subAccountRepository = new();
    private readonly Mock<ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>>
        processTransferStatusHandler = new();
    private readonly Mock<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>> processDepositHandler = new();
    private readonly Mock<ISettableCallerContext> callerContext = new();

    private TransfersWebhookTopicProcessor CreateSut() => new(
        transferRepository.Object,
        subAccountRepository.Object,
        processTransferStatusHandler.Object,
        processDepositHandler.Object,
        callerContext.Object);

    private static SubAccount ActiveSubAccount(string circleWalletId)
    {
        var subAccount = SubAccount.Create("client-1", DateTime.UtcNow);
        subAccount.BeginCompliance(circleWalletId);
        subAccount.MarkAccepted();
        return subAccount;
    }

    private static Transfer ExistingTransfer(string circleTransferId)
    {
        var transfer = Transfer.Create(
            Guid.NewGuid(), "client-1", Guid.NewGuid(), new Money(50m, "USDC"), "corr-1", DateTime.UtcNow);
        transfer.SetProviderReference(circleTransferId, DateTime.UtcNow);
        return transfer;
    }

    [Fact]
    public void Topic_IsTransfers()
    {
        CreateSut().Topic.Should().Be("transfers");
    }

    [Fact]
    public async Task ProcessAsync_WhenLocalTransferRowExists_DispatchesProcessTransferStatusCommand_WithRawStatus()
    {
        var existingTransfer = ExistingTransfer("transfer-abc");
        transferRepository
            .Setup(x => x.FindByCircleTransferIdAsync("transfer-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTransfer);
        processTransferStatusHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessTransferStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessTransferStatusResult(existingTransfer.Id, TransferStatus.Pending));

        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "transfers",
              "version": 2,
              "transfer": {
                "id": "transfer-abc",
                "status": "running",
                "source": { "type": "wallet", "id": "wallet-source" },
                "destination": { "type": "verified_blockchain", "id": "wallet-source" },
                "amount": { "amount": "50.00", "currency": "USDC" }
              }
            }
            """;

        await CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        // The raw "running" literal is forwarded as-is — TransferStatusMapper inside the handler
        // owns the running -> Pending collapse, not the processor.
        processTransferStatusHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessTransferStatusCommand>(cmd =>
                    cmd.CircleTransferId == "transfer-abc" && cmd.Status == "running"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        processDepositHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessDepositCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        callerContext.Verify(x => x.Set(It.IsAny<string>(), It.IsAny<CallerRole>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenNoLocalTransferRow_ResolvesSubAccountFromDestination_AndDispatchesDeposit()
    {
        var subAccount = ActiveSubAccount("wallet-dest");
        transferRepository
            .Setup(x => x.FindByCircleTransferIdAsync("transfer-xyz", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transfer?)null);
        subAccountRepository
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-dest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);
        processDepositHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessDepositCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDepositResult(
                Guid.NewGuid(), subAccount.Id, new Money(25m, "USDC"), TransactionStatus.Complete, DateTime.UtcNow));

        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "transfers",
              "version": 2,
              "transfer": {
                "id": "transfer-xyz",
                "status": "complete",
                "source": { "type": "blockchain", "id": "0xabc" },
                "destination": { "type": "wallet", "id": "wallet-dest" },
                "amount": { "amount": "25.00", "currency": "USDC" }
              }
            }
            """;

        await CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        callerContext.Verify(x => x.Set("client-1", CallerRole.SubAccount), Times.Once);
        processDepositHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessDepositCommand>(cmd =>
                    cmd.SubAccountId == subAccount.Id
                    && cmd.ProviderReferenceId == "transfer-xyz"
                    && cmd.DepositSourceType == DepositSourceType.OnChain
                    && cmd.Amount.Amount == 25.00m
                    && cmd.Amount.CurrencyCode == "USDC"
                    && cmd.CorrelationId == "transfer-xyz"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        processTransferStatusHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessTransferStatusCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_IncomingBranch_WhenWalletIdDoesNotResolveToASubAccount_ThrowsDepositSourceNotResolvedException()
    {
        transferRepository
            .Setup(x => x.FindByCircleTransferIdAsync("transfer-xyz", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transfer?)null);
        subAccountRepository
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);

        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "transfers",
              "version": 2,
              "transfer": {
                "id": "transfer-xyz",
                "status": "complete",
                "destination": { "type": "wallet", "id": "wallet-unknown" },
                "amount": { "amount": "25.00", "currency": "USDC" }
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DepositSourceNotResolvedException>();
        processDepositHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessDepositCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_IncomingBranch_WhenDestinationMissing_ThrowsDepositSourceNotResolvedException()
    {
        transferRepository
            .Setup(x => x.FindByCircleTransferIdAsync("transfer-xyz", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transfer?)null);

        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "transfers",
              "version": 2,
              "transfer": {
                "id": "transfer-xyz",
                "status": "complete",
                "amount": { "amount": "25.00", "currency": "USDC" }
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DepositSourceNotResolvedException>();
        subAccountRepository.Verify(
            x => x.GetByCircleWalletIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenPayloadMissingTransfer_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "transfers",
              "version": 2
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        transferRepository.Verify(
            x => x.FindByCircleTransferIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenIdOrStatusMissing_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "transfers",
              "version": 2,
              "transfer": {
                "id": "",
                "status": "complete"
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        transferRepository.Verify(
            x => x.FindByCircleTransferIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
