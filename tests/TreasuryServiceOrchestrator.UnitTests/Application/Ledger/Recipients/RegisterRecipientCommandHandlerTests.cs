using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Recipients;

public sealed class RegisterRecipientCommandHandlerTests
{
    private readonly Mock<IRecipientRepository> recipients = new();
    private readonly Mock<IStablecoinGateway> gateway = new();
    private readonly Mock<IIdempotencyService> idempotency = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly RegisterRecipientCommandHandler handler;

    public RegisterRecipientCommandHandlerTests()
    {
        idempotency
            .Setup(x => x.TryBeginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Started());
        callerContext.Setup(x => x.CallerId).Returns("client-1");

        handler = new RegisterRecipientCommandHandler(
            recipients.Object,
            gateway.Object,
            idempotency.Object,
            unitOfWork.Object,
            new RegisterRecipientCommandValidator(),
            TimeProvider.System,
            callerContext.Object);
    }

    private static RegisterRecipientCommand ValidCommand(Guid? subAccountId = null) => new(
        SubAccountId: subAccountId ?? Guid.NewGuid(),
        Chain: "ETH",
        Address: "0xabc",
        Label: "My wallet");

    [Fact]
    public async Task HandleAsync_WithNoCachedResult_ReservesIdempotencyKeyCallsGatewayAndPersists()
    {
        var command = ValidCommand();
        gateway
            .Setup(x => x.RegisterRecipientAsync(
                It.IsAny<RegisterRecipientGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredRecipient("circle-recipient-1", "pending_verification"));

        var expectedKey = $"recipient:client-1:ETH:0xabc";

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.SubAccountId.Should().Be(command.SubAccountId);
        result.CircleRecipientId.Should().Be("circle-recipient-1");
        result.Status.Should().Be(RecipientStatus.PendingApproval);
        idempotency.Verify(
            x => x.TryBeginAsync(
                "client-1", expectedKey, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        recipients.Verify(
            x => x.AddAsync(It.IsAny<Recipient>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task HandleAsync_WithCachedIdempotencyResult_ReturnsCachedResultWithoutCallingGateway()
    {
        var command = ValidCommand();
        var cachedResult = new RegisterRecipientResult(
            Guid.NewGuid(), command.SubAccountId, "ETH", "0xabc", "My wallet", "circle-recipient-cached",
            RecipientStatus.PendingApproval, DateTime.UtcNow);
        var expectedKey = $"recipient:client-1:ETH:0xabc";
        idempotency
            .Setup(x => x.TryBeginAsync(
                "client-1", expectedKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Replay(System.Text.Json.JsonSerializer.Serialize(cachedResult)));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.CircleRecipientId.Should().Be("circle-recipient-cached");
        gateway.Verify(
            x => x.RegisterRecipientAsync(
                It.IsAny<RegisterRecipientGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        recipients.Verify(
            x => x.AddAsync(It.IsAny<Recipient>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyAddress_ThrowsValidationExceptionWithoutTouchingGateway()
    {
        var command = ValidCommand() with { Address = string.Empty };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        gateway.Verify(
            x => x.RegisterRecipientAsync(
                It.IsAny<RegisterRecipientGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithEmptySubAccountId_ThrowsValidationException()
    {
        var command = ValidCommand() with { SubAccountId = Guid.Empty };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
