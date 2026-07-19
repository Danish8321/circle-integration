using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Compliance.ResubmitEntityRegistration;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Compliance;

public sealed class ResubmitEntityRegistrationHandlerTests
{
    private readonly Mock<ISubAccountGateway> gateway = new();
    private readonly Mock<IIdempotencyService> idempotency = new();
    private readonly Mock<IAuditLogService> auditLog = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IEntityRegistrationRepository> entityRegistrations = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ResubmitEntityRegistrationHandler handler;

    public ResubmitEntityRegistrationHandlerTests()
    {
        idempotency
            .Setup(x => x.TryBeginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Started());
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        callerContext.Setup(x => x.Role).Returns(CallerRole.SubAccount);
        callerContext.Setup(x => x.IsAdmin).Returns(false);

        handler = new ResubmitEntityRegistrationHandler(
            gateway.Object,
            idempotency.Object,
            auditLog.Object,
            unitOfWork.Object,
            subAccounts.Object,
            entityRegistrations.Object,
            new ResubmitEntityRegistrationValidator(),
            TimeProvider.System,
            callerContext.Object);
    }

    private static ResubmitEntityRegistrationCommand ValidCommand() => new(
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

    private static SubAccount RejectedSubAccount()
    {
        var subAccount = SubAccount.Create("client-1", DateTime.UtcNow);
        subAccount.BeginCompliance("wallet-original");
        subAccount.MarkRejected();
        return subAccount;
    }

    private static SubAccount PendingSubAccount()
    {
        var subAccount = SubAccount.Create("client-1", DateTime.UtcNow);
        subAccount.BeginCompliance("wallet-original");
        return subAccount;
    }

    [Fact]
    public async Task HandleAsync_WithRejectedSubAccount_ResubmitsAndReturnsPendingCompliance()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RejectedSubAccount());
        gateway
            .Setup(x => x.CreateExternalEntityAsync(
                It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateExternalEntityResult("wallet-123", "PENDING", "Acme Inc", "EIN-123"));

        var result = await handler.HandleAsync(ValidCommand(), TestContext.Current.CancellationToken);

        result.LifecycleState.Should().Be(SubAccountLifecycleState.PendingCompliance.ToString());
        result.RegistrationStatus.Should().Be(EntityRegistrationStatus.Pending.ToString());
        result.RegistrationId.Should().NotBe(Guid.Empty);
        gateway.Verify(
            x => x.CreateExternalEntityAsync(
                It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        entityRegistrations.Verify(
            x => x.AddAsync(It.IsAny<EntityRegistration>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task HandleAsync_WithNonRejectedSubAccount_ThrowsConflictWithoutCallingGateway()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingSubAccount());

        var act = () => handler.HandleAsync(ValidCommand());

        await act.Should().ThrowAsync<ConflictException>();
        gateway.Verify(
            x => x.CreateExternalEntityAsync(
                It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNoSubAccount_ThrowsNotFoundWithoutCallingGateway()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);

        var act = () => handler.HandleAsync(ValidCommand());

        await act.Should().ThrowAsync<NotFoundException>();
        gateway.Verify(
            x => x.CreateExternalEntityAsync(
                It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithCachedIdempotencyResult_ReturnsCachedResultWithoutCallingGateway()
    {
        var cachedResult = new ResubmitEntityRegistrationResult(
            Guid.NewGuid(), "client-1", Guid.NewGuid(),
            SubAccountLifecycleState.PendingCompliance.ToString(),
            EntityRegistrationStatus.Pending.ToString());
        idempotency
            .Setup(x => x.TryBeginAsync(
                "client-1", "idem-key-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Replay(System.Text.Json.JsonSerializer.Serialize(cachedResult)));

        var result = await handler.HandleAsync(ValidCommand(), TestContext.Current.CancellationToken);

        result.RegistrationId.Should().Be(cachedResult.RegistrationId);
        gateway.Verify(
            x => x.CreateExternalEntityAsync(
                It.IsAny<CreateExternalEntityGatewayRequest>(), It.IsAny<CancellationToken>()),
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
