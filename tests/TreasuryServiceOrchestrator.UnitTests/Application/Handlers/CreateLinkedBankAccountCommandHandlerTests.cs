using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class CreateLinkedBankAccountCommandHandlerTests
{
    private readonly Mock<ILinkedBankAccountRepository> linkedBankAccounts = new();
    private readonly Mock<IStablecoinGateway> gateway = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly CreateLinkedBankAccountCommandHandler handler;

    public CreateLinkedBankAccountCommandHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");

        handler = new CreateLinkedBankAccountCommandHandler(
            linkedBankAccounts.Object,
            gateway.Object,
            unitOfWork.Object,
            new CreateLinkedBankAccountCommandValidator(),
            TimeProvider.System,
            callerContext.Object);
    }

    private static CreateLinkedBankAccountCommand ValidCommand(Guid? subAccountId = null) => new(
        SubAccountId: subAccountId ?? Guid.NewGuid(),
        BeneficiaryName: "Acme Inc",
        AccountNumber: "12345678",
        RoutingNumber: "021000021",
        BankName: "Chase",
        BillingName: "Acme Inc",
        BillingCity: "New York",
        BillingCountry: "US",
        BillingLine1: "123 Main St",
        BillingPostalCode: "10001",
        BillingLine2: null,
        BillingDistrict: "NY",
        BankAddressCountry: "US",
        BankAddressBankName: null,
        IdempotencyKey: "idem-1");

    [Fact]
    public async Task HandleAsync_WithPendingGatewayResult_PersistsWithPendingStatusAndSingleSave()
    {
        var command = ValidCommand();
        gateway
            .Setup(x => x.CreateLinkedBankAccountAsync(
                It.IsAny<CreateLinkedBankAccountGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedLinkedBankAccount("circle-bank-1", "pending"));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.SubAccountId.Should().Be(command.SubAccountId);
        result.CircleBankAccountId.Should().Be("circle-bank-1");
        result.Status.Should().Be(LinkedBankAccountStatus.Pending);
        linkedBankAccounts.Verify(
            x => x.AddAsync(It.IsAny<LinkedBankAccount>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithCompleteGatewayResult_PersistsWithActiveStatus()
    {
        var command = ValidCommand();
        gateway
            .Setup(x => x.CreateLinkedBankAccountAsync(
                It.IsAny<CreateLinkedBankAccountGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedLinkedBankAccount("circle-bank-1", "complete"));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.Status.Should().Be(LinkedBankAccountStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_ForwardsIdempotencyKeyToGateway()
    {
        var command = ValidCommand();
        gateway
            .Setup(x => x.CreateLinkedBankAccountAsync(
                It.IsAny<CreateLinkedBankAccountGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedLinkedBankAccount("circle-bank-1", "pending"));

        await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        gateway.Verify(
            x => x.CreateLinkedBankAccountAsync(
                It.Is<CreateLinkedBankAccountGatewayRequest>(r => r.IdempotencyKey == "idem-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyBeneficiaryName_ThrowsValidationExceptionWithoutTouchingGateway()
    {
        var command = ValidCommand() with { BeneficiaryName = string.Empty };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        gateway.Verify(
            x => x.CreateLinkedBankAccountAsync(
                It.IsAny<CreateLinkedBankAccountGatewayRequest>(), It.IsAny<CancellationToken>()),
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
