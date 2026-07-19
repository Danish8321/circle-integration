using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Compliance.GetSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.UnitTests.Compliance;

public sealed class GetSubAccountHandlerTests
{
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IEntityRegistrationRepository> entityRegistrations = new();
    private readonly GetSubAccountHandler handler;

    public GetSubAccountHandlerTests()
    {
        handler = new GetSubAccountHandler(subAccounts.Object, entityRegistrations.Object);
    }

    private static SubAccount ProvisionedSubAccount(string clientCompanyId)
    {
        var subAccount = SubAccount.Create(clientCompanyId, new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc));
        subAccount.BeginCompliance("wallet-123");
        return subAccount;
    }

    [Fact]
    public async Task HandleAsync_WithSubAccountAndRegistration_MapsEveryField()
    {
        var subAccount = ProvisionedSubAccount("client-1");
        var nowUtc = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);
        var registration = EntityRegistration.Create(
            subAccount.Id, "client-1", "Acme Inc", "EIN-123", "US",
            "US", "NY", "New York", "10001", "Broadway", "1", "wallet-123", nowUtc);
        registration.Reject("Sanctions hit", nowUtc);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(subAccount.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(registration);

        var result = await handler.HandleAsync(
            new GetSubAccountQuery("client-1"), TestContext.Current.CancellationToken);

        result.SubAccountId.Should().Be(subAccount.Id);
        result.ClientCompanyId.Should().Be("client-1");
        result.LifecycleState.Should().Be("PendingCompliance");
        result.IsDisabled.Should().BeFalse();
        result.CircleWalletId.Should().Be("wallet-123");
        result.LatestRegistrationStatus.Should().Be("Rejected");
        result.RegistrationRejectionReason.Should().Be("Sanctions hit");
    }

    [Fact]
    public async Task HandleAsync_WithSubAccountButNoRegistration_ReturnsNullStatusAndReason()
    {
        var subAccount = ProvisionedSubAccount("client-2");
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(subAccount.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityRegistration?)null);

        var result = await handler.HandleAsync(
            new GetSubAccountQuery("client-2"), TestContext.Current.CancellationToken);

        result.LatestRegistrationStatus.Should().BeNull();
        result.RegistrationRejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownClientCompanyId_ThrowsNotFound()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);

        var act = () => handler.HandleAsync(
            new GetSubAccountQuery("missing"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("No sub-account for client company 'missing'.");
    }
}
