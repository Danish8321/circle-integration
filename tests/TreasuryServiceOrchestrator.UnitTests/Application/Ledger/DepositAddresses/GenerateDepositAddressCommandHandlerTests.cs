using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.DepositAddresses;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.DepositAddresses;

public sealed class GenerateDepositAddressCommandHandlerTests
{
    private readonly Mock<IDepositAddressRepository> depositAddresses = new();
    private readonly Mock<IStablecoinGateway> gateway = new();
    private readonly Mock<IIdempotencyService> idempotency = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GenerateDepositAddressCommandHandler handler;

    public GenerateDepositAddressCommandHandlerTests()
    {
        idempotency
            .Setup(x => x.TryBeginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Started());
        callerContext.Setup(x => x.CallerId).Returns("client-1");

        handler = new GenerateDepositAddressCommandHandler(
            depositAddresses.Object,
            gateway.Object,
            idempotency.Object,
            unitOfWork.Object,
            new GenerateDepositAddressCommandValidator(new SupportedChainsOptions { Chains = ["ETH"] }),
            TimeProvider.System,
            callerContext.Object);
    }

    private static GenerateDepositAddressCommand ValidCommand(Guid? subAccountId = null) => new(
        SubAccountId: subAccountId ?? Guid.NewGuid(),
        Chain: "ETH",
        Currency: "USDC");

    [Fact]
    public async Task HandleAsync_WithNoExistingAddress_ReservesIdempotencyKeyBeforeCallingGateway()
    {
        var command = ValidCommand();
        depositAddresses
            .Setup(x => x.FindAsync(command.SubAccountId, "ETH", "USDC", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepositAddress?)null);
        gateway
            .Setup(x => x.GenerateDepositAddressAsync(
                It.IsAny<GenerateDepositAddressGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedDepositAddress("0xabc", "ETH", "USDC", "circle-addr-1"));

        var expectedKey = $"deposit-address:{command.SubAccountId}:ETH:USDC";

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.Address.Should().Be("0xabc");
        result.SubAccountId.Should().Be(command.SubAccountId);
        idempotency.Verify(
            x => x.TryBeginAsync(
                "client-1", expectedKey, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        depositAddresses.Verify(
            x => x.AddAsync(It.IsAny<DepositAddress>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task HandleAsync_WithCachedIdempotencyResult_ReturnsCachedResultWithoutCallingGateway()
    {
        var command = ValidCommand();
        var cachedResult = new GenerateDepositAddressResult(
            Guid.NewGuid(), command.SubAccountId, "ETH", "USDC", "0xcached", DateTime.UtcNow);
        var expectedKey = $"deposit-address:{command.SubAccountId}:ETH:USDC";
        idempotency
            .Setup(x => x.TryBeginAsync(
                "client-1", expectedKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Replay(System.Text.Json.JsonSerializer.Serialize(cachedResult)));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.Address.Should().Be("0xcached");
        gateway.Verify(
            x => x.GenerateDepositAddressAsync(
                It.IsAny<GenerateDepositAddressGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        depositAddresses.Verify(
            x => x.FindAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithExistingLocalAddress_ReturnsExistingWithoutCallingGateway()
    {
        var command = ValidCommand();
        var existing = DepositAddress.Create(
            command.SubAccountId, "ETH", "USDC", "0xexisting", "circle-addr-existing", DateTime.UtcNow);
        depositAddresses
            .Setup(x => x.FindAsync(command.SubAccountId, "ETH", "USDC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.Address.Should().Be("0xexisting");
        gateway.Verify(
            x => x.GenerateDepositAddressAsync(
                It.IsAny<GenerateDepositAddressGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        depositAddresses.Verify(
            x => x.AddAsync(It.IsAny<DepositAddress>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnsupportedChain_ThrowsValidationExceptionWithoutTouchingRepository()
    {
        var command = ValidCommand() with { Chain = "SOL" };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        depositAddresses.Verify(
            x => x.FindAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        gateway.Verify(
            x => x.GenerateDepositAddressAsync(
                It.IsAny<GenerateDepositAddressGatewayRequest>(), It.IsAny<CancellationToken>()),
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
