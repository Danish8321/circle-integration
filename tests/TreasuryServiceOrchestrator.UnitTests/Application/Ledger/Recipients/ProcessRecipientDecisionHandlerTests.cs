using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Recipients;

public sealed class ProcessRecipientDecisionHandlerTests
{
    private readonly Mock<IRecipientRepository> recipients = new();
    private readonly Mock<INotificationOutboxRepository> outbox = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly ProcessRecipientDecisionHandler handler;

    public ProcessRecipientDecisionHandlerTests()
    {
        handler = new ProcessRecipientDecisionHandler(
            recipients.Object, outbox.Object, unitOfWork.Object, TimeProvider.System);
    }

    private static Recipient PendingRecipient() => Recipient.Create(
        Guid.NewGuid(), "client-1", "ETH", "0xabc", "My wallet", "circle-recipient-1",
        RecipientStatus.PendingApproval, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_WithActiveDecision_UpdatesToActiveWithNullDenialReason()
    {
        var recipient = PendingRecipient();
        recipients
            .Setup(x => x.FindByCircleRecipientIdAsync("circle-recipient-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);

        var result = await handler.HandleAsync(
            new ProcessRecipientDecisionCommand("circle-recipient-1", "active"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(RecipientStatus.Active);
        recipient.Status.Should().Be(RecipientStatus.Active);
        recipient.DenialReason.Should().BeNull();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(
            x => x.AddAsync(
                It.Is<NotificationOutboxEntry>(e => e.EventType == "RecipientApprovalDecided"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithDeniedDecision_UpdatesToDeniedWithDenialReasonSet()
    {
        var recipient = PendingRecipient();
        recipients
            .Setup(x => x.FindByCircleRecipientIdAsync("circle-recipient-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);

        var result = await handler.HandleAsync(
            new ProcessRecipientDecisionCommand("circle-recipient-1", "denied"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(RecipientStatus.Denied);
        recipient.Status.Should().Be(RecipientStatus.Denied);
        recipient.DenialReason.Should().NotBeNullOrEmpty();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithUnchangedStatus_IsNoOpAndDoesNotSave()
    {
        var recipient = PendingRecipient();
        recipients
            .Setup(x => x.FindByCircleRecipientIdAsync("circle-recipient-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);

        var result = await handler.HandleAsync(
            new ProcessRecipientDecisionCommand("circle-recipient-1", "pending"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(RecipientStatus.PendingApproval);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownStatusLiteral_FallsBackToPendingApprovalWithoutThrowing()
    {
        var recipient = PendingRecipient();
        recipients
            .Setup(x => x.FindByCircleRecipientIdAsync("circle-recipient-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);

        var act = () => handler.HandleAsync(
            new ProcessRecipientDecisionCommand("circle-recipient-1", "some_future_literal"),
            TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();

        var result = await handler.HandleAsync(
            new ProcessRecipientDecisionCommand("circle-recipient-1", "some_future_literal"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(RecipientStatus.PendingApproval);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownCircleRecipientId_ThrowsNotFound()
    {
        recipients
            .Setup(x => x.FindByCircleRecipientIdAsync("unknown-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Recipient?)null);

        var act = () => handler.HandleAsync(
            new ProcessRecipientDecisionCommand("unknown-id", "active"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
