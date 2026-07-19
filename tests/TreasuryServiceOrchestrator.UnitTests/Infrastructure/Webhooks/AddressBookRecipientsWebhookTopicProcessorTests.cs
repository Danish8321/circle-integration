using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Webhooks;

/// <summary>
/// Ticket 05.4: <see cref="AddressBookRecipientsWebhookTopicProcessor"/> parses Circle's real
/// SNS-unwrapped "addressBookRecipients" envelope
/// (docs/features/10-outbound-transfers-and-recipients.md §3.2) and dispatches into
/// <see cref="ProcessRecipientDecisionHandler"/> via <see cref="ICommandHandler{TCommand,TResult}"/>.
/// </summary>
public sealed class AddressBookRecipientsWebhookTopicProcessorTests
{
    private readonly Mock<ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>>
        processRecipientDecisionHandler = new();

    private AddressBookRecipientsWebhookTopicProcessor CreateSut() =>
        new(processRecipientDecisionHandler.Object);

    [Fact]
    public void Topic_IsAddressBookRecipients()
    {
        CreateSut().Topic.Should().Be("addressBookRecipients");
    }

    [Fact]
    public async Task ProcessAsync_ParsesEnvelope_AndDispatchesRecipientDecision()
    {
        processRecipientDecisionHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessRecipientDecisionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRecipientDecisionResult(Guid.NewGuid(), RecipientStatus.Active));

        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "addressBookRecipients",
              "version": 2,
              "addressBookRecipient": {
                "id": "recipient-abc",
                "status": "active"
              }
            }
            """;

        await CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        processRecipientDecisionHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessRecipientDecisionCommand>(cmd =>
                    cmd.CircleRecipientId == "recipient-abc"
                    && cmd.Status == "active"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenPayloadMissingAddressBookRecipient_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "addressBookRecipients",
              "version": 2
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        processRecipientDecisionHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessRecipientDecisionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenIdMissing_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "addressBookRecipients",
              "version": 2,
              "addressBookRecipient": {
                "id": "",
                "status": "active"
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        processRecipientDecisionHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessRecipientDecisionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenStatusMissing_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "clientId": "client-1",
              "notificationType": "addressBookRecipients",
              "version": 2,
              "addressBookRecipient": {
                "id": "recipient-abc"
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        processRecipientDecisionHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessRecipientDecisionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
