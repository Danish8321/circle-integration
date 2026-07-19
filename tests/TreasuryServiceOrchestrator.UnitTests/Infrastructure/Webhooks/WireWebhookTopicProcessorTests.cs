using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Webhooks;

/// <summary>
/// Ticket 07.5: <see cref="WireWebhookTopicProcessor"/> parses Circle's real SNS-unwrapped
/// "wire" envelope (docs/features/08-banking-and-wire-instructions.md §8) and dispatches into
/// <see cref="ProcessLinkedBankAccountStatusCommand"/> via
/// <see cref="ICommandHandler{TCommand,TResult}"/>.
/// </summary>
public sealed class WireWebhookTopicProcessorTests
{
    private readonly Mock<ICommandHandler<ProcessLinkedBankAccountStatusCommand, ProcessLinkedBankAccountStatusResult>>
        processLinkedBankAccountStatusHandler = new();

    private WireWebhookTopicProcessor CreateSut() =>
        new(processLinkedBankAccountStatusHandler.Object);

    [Fact]
    public void Topic_IsWire()
    {
        CreateSut().Topic.Should().Be("wire");
    }

    [Fact]
    public async Task ProcessAsync_ParsesEnvelope_AndDispatchesRawStatusLiteral()
    {
        processLinkedBankAccountStatusHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessLinkedBankAccountStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessLinkedBankAccountStatusResult(Guid.NewGuid(), LinkedBankAccountStatus.Active));

        var payload = """
            {
              "wire": {
                "id": "bank-account-abc",
                "status": "complete"
              }
            }
            """;

        await CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        processLinkedBankAccountStatusHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessLinkedBankAccountStatusCommand>(cmd =>
                    cmd.CircleBankAccountId == "bank-account-abc"
                    && cmd.Status == "complete"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenPayloadMissingWire_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "notificationType": "wire"
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        processLinkedBankAccountStatusHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessLinkedBankAccountStatusCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenIdMissing_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "wire": {
                "id": "",
                "status": "complete"
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        processLinkedBankAccountStatusHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessLinkedBankAccountStatusCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenStatusMissing_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "wire": {
                "id": "bank-account-abc"
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        processLinkedBankAccountStatusHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessLinkedBankAccountStatusCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
