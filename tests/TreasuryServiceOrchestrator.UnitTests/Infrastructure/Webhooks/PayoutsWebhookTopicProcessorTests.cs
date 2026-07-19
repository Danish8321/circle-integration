using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Webhooks;

/// <summary>
/// Ticket 07.5: <see cref="PayoutsWebhookTopicProcessor"/> parses Circle's real SNS-unwrapped
/// "payouts" envelope (docs/features/11-redemption-and-payouts.md §7) and dispatches into
/// <see cref="ProcessPayoutStatusCommand"/> via <see cref="ICommandHandler{TCommand,TResult}"/>.
/// The optional-<c>toAmount</c>-vs-computed-fallback branch (correction #3) is resolved here.
/// </summary>
public sealed class PayoutsWebhookTopicProcessorTests
{
    private readonly Mock<ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>>
        processPayoutStatusHandler = new();

    private PayoutsWebhookTopicProcessor CreateSut() =>
        new(processPayoutStatusHandler.Object);

    [Fact]
    public void Topic_IsPayouts()
    {
        CreateSut().Topic.Should().Be("payouts");
    }

    [Fact]
    public async Task ProcessAsync_WhenToAmountPresent_UsesToAmountAsNetAmount()
    {
        processPayoutStatusHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessPayoutStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessPayoutStatusResult(Guid.NewGuid(), TransferStatus.Complete));

        var payload = """
            {
              "payout": {
                "id": "redeem-abc",
                "status": "complete",
                "amount": "100.00",
                "fees": "1.50",
                "toAmount": "98.25",
                "currency": "USD"
              }
            }
            """;

        await CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        processPayoutStatusHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessPayoutStatusCommand>(cmd =>
                    cmd.CircleRedeemId == "redeem-abc"
                    && cmd.Status == "complete"
                    && cmd.Fees == new Money(1.50m, "USD")
                    && cmd.NetAmount == new Money(98.25m, "USD")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenToAmountAbsent_ComputesNetAmountAsAmountMinusFees()
    {
        processPayoutStatusHandler
            .Setup(x => x.HandleAsync(It.IsAny<ProcessPayoutStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessPayoutStatusResult(Guid.NewGuid(), TransferStatus.Complete));

        var payload = """
            {
              "payout": {
                "id": "redeem-abc",
                "status": "complete",
                "amount": "100.00",
                "fees": "1.50",
                "currency": "USD"
              }
            }
            """;

        await CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        processPayoutStatusHandler.Verify(
            x => x.HandleAsync(
                It.Is<ProcessPayoutStatusCommand>(cmd =>
                    cmd.CircleRedeemId == "redeem-abc"
                    && cmd.Status == "complete"
                    && cmd.Fees == new Money(1.50m, "USD")
                    && cmd.NetAmount == new Money(98.50m, "USD")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenAmountMissing_ThrowsInvalidOperationException()
    {
        var payload = """
            {
              "payout": {
                "id": "redeem-abc",
                "status": "complete",
                "fees": "1.50",
                "currency": "USD"
              }
            }
            """;

        var act = () => CreateSut().ProcessAsync(payload, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        processPayoutStatusHandler.Verify(
            x => x.HandleAsync(It.IsAny<ProcessPayoutStatusCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
