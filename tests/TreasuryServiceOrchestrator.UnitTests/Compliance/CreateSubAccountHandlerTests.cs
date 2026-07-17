using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Compliance;

public sealed class CreateSubAccountHandlerTests
{
    private readonly Mock<ISubAccountGateway> gateway = new();
    private readonly Mock<IIdempotencyService> idempotency = new();
    private readonly Mock<IAuditLogService> auditLog = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IEntityRegistrationRepository> entityRegistrations = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly CreateSubAccountHandler handler;

    public CreateSubAccountHandlerTests()
    {
        idempotency
            .Setup(x => x.TryGetCachedResultJsonAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        callerContext.Setup(x => x.CallerId).Returns("apiso-admin");
        callerContext.Setup(x => x.Role).Returns(CallerRole.Admin);
        callerContext.Setup(x => x.IsAdmin).Returns(true);

        handler = new CreateSubAccountHandler(
            gateway.Object,
            idempotency.Object,
            auditLog.Object,
            unitOfWork.Object,
            subAccounts.Object,
            entityRegistrations.Object,
            new CreateSubAccountValidator(),
            TimeProvider.System,
            callerContext.Object);
    }

    private static CreateSubAccountCommand ValidCommand() => new(
        ClientCompanyId: "client-1",
        BusinessName: "Acme Inc",
        BusinessUniqueIdentifier: "EIN-123",
        IdentifierIssuingCountryCode: "US",
        Country: "US",
        State: "NY",
        City: "New York",
        Postcode: "10001",
        StreetName: "Broadway",
        BuildingNumber: "1",
        IdempotencyKey: "idem-key-1",
        CorrelationId: "corr-1");

    [Fact]
    public async Task HandleAsync_WithNoExistingSubAccount_ProvisionsAndReturnsPendingCompliance()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);
        gateway
            .Setup(x => x.CreateExternalEntityAsync(It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateExternalEntityResult("wallet-123", "PENDING", "Acme Inc", "EIN-123"));

        var result = await handler.HandleAsync(ValidCommand(), TestContext.Current.CancellationToken);

        result.CircleWalletId.Should().Be("wallet-123");
        result.LifecycleState.Should().Be(SubAccountLifecycleState.PendingCompliance);
        subAccounts.Verify(x => x.AddAsync(It.IsAny<SubAccount>(), It.IsAny<CancellationToken>()), Times.Once);
        entityRegistrations.Verify(
            x => x.AddAsync(It.IsAny<EntityRegistration>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WithExistingSubAccountForClient_ThrowsWithoutCallingGateway()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubAccount.Create("client-1", DateTime.UtcNow));

        var act = () => handler.HandleAsync(ValidCommand());

        await act.Should().ThrowAsync<SubAccountAlreadyExistsException>();
        gateway.Verify(
            x => x.CreateExternalEntityAsync(It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithCachedIdempotencyResult_ReturnsCachedResultWithoutCallingGateway()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);
        var cachedResult = new CreateSubAccountResult(
            Guid.NewGuid(), "client-1", "wallet-cached", SubAccountLifecycleState.PendingCompliance);
        idempotency
            .Setup(x => x.TryGetCachedResultJsonAsync(
                "client-1", "idem-key-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Json.JsonSerializer.Serialize(cachedResult));

        var result = await handler.HandleAsync(ValidCommand(), TestContext.Current.CancellationToken);

        result.CircleWalletId.Should().Be("wallet-cached");
        gateway.Verify(
            x => x.CreateExternalEntityAsync(It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNonAdminCaller_ThrowsTenantForbiddenWithoutCallingGateway()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        callerContext.Setup(x => x.Role).Returns(CallerRole.SubAccount);
        callerContext.Setup(x => x.IsAdmin).Returns(false);

        var act = () => handler.HandleAsync(ValidCommand());

        await act.Should().ThrowAsync<TenantForbiddenException>();
        gateway.Verify(
            x => x.CreateExternalEntityAsync(It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithBlankBusinessName_ThrowsValidationExceptionWithoutTouchingRepositories()
    {
        var command = ValidCommand() with { BusinessName = "" };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        subAccounts.Verify(
            x => x.GetByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
