using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Compliance;

public sealed class ListSubAccountsHandlerTests
{
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IEntityRegistrationRepository> entityRegistrations = new();
    private readonly Mock<IFundAccountRepository> fundAccounts = new();
    private readonly Mock<IAuditLogService> auditLog = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ListSubAccountsHandler handler;

    public ListSubAccountsHandlerTests()
    {
        callerContext.SetupGet(x => x.CallerId).Returns("apiso-admin");
        callerContext.SetupGet(x => x.Role).Returns(CallerRole.Admin);
        callerContext.SetupGet(x => x.IsAdmin).Returns(true);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundAccount?)null);
        handler = new ListSubAccountsHandler(
            subAccounts.Object, entityRegistrations.Object, fundAccounts.Object, auditLog.Object,
            unitOfWork.Object, callerContext.Object);
    }

    private static ListSubAccountsQuery AllTenantsQuery(SubAccountLifecycleState? lifecycleState = null) =>
        new(new TenantScope.AllTenants(), lifecycleState, "corr-1");

    private static SubAccount ProvisionedSubAccount(string clientCompanyId)
    {
        var subAccount = SubAccount.Create(clientCompanyId, new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc));
        subAccount.BeginCompliance($"wallet-{clientCompanyId}");
        return subAccount;
    }

    [Fact]
    public async Task HandleAsync_AsAdminWithAllTenantsScope_MapsEveryElementWithLatestRegistration()
    {
        var first = ProvisionedSubAccount("client-1");
        var second = ProvisionedSubAccount("client-2");
        var nowUtc = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);
        var firstRegistration = EntityRegistration.Create(
            first.Id, "client-1", "Acme Inc", "EIN-123", "US",
            "US", "NY", "New York", "10001", "Broadway", "1", "wallet-client-1", nowUtc);
        firstRegistration.Reject("Sanctions hit", nowUtc);
        subAccounts
            .Setup(x => x.ListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(first.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstRegistration);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(second.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityRegistration?)null);

        var results = await handler.HandleAsync(AllTenantsQuery(), TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
        results[0].SubAccountId.Should().Be(first.Id);
        results[0].ClientCompanyId.Should().Be("client-1");
        results[0].LifecycleState.Should().Be("PendingCompliance");
        results[0].IsDisabled.Should().BeFalse();
        results[0].CircleWalletId.Should().Be("wallet-client-1");
        results[0].LatestRegistrationStatus.Should().Be("Rejected");
        results[0].RegistrationRejectionReason.Should().Be("Sanctions hit");
        results[1].SubAccountId.Should().Be(second.Id);
        results[1].LatestRegistrationStatus.Should().BeNull();
        results[1].RegistrationRejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithSingleTenantScope_ThrowsTenantForbidden()
    {
        var act = () => handler.HandleAsync(
            new ListSubAccountsQuery(new TenantScope.SingleTenant("client-1"), null, "corr-1"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        subAccounts.Verify(
            x => x.ListAsync(It.IsAny<SubAccountLifecycleState?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AsNonAdminCaller_ThrowsTenantForbidden()
    {
        callerContext.SetupGet(x => x.CallerId).Returns("client-1");
        callerContext.SetupGet(x => x.Role).Returns(CallerRole.SubAccount);
        callerContext.SetupGet(x => x.IsAdmin).Returns(false);

        var act = () => handler.HandleAsync(AllTenantsQuery(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        subAccounts.Verify(
            x => x.ListAsync(It.IsAny<SubAccountLifecycleState?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithLifecycleStateFilter_PassesFilterToRepository()
    {
        subAccounts
            .Setup(x => x.ListAsync(SubAccountLifecycleState.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            AllTenantsQuery(SubAccountLifecycleState.Active), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
        subAccounts.Verify(
            x => x.ListAsync(SubAccountLifecycleState.Active, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenFundAccountExists_PopulatesCurrentBalance()
    {
        var subAccount = ProvisionedSubAccount("client-1");
        var fundAccount = FundAccount.Create(
            "client-1", new Money(42m, "USDC"), new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc));
        subAccounts
            .Setup(x => x.ListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([subAccount]);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(subAccount.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityRegistration?)null);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fundAccount);

        var results = await handler.HandleAsync(AllTenantsQuery(), TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        results[0].CurrentBalance.Should().Be(new Money(42m, "USDC"));
    }

    [Fact]
    public async Task HandleAsync_WhenNoFundAccountExists_LeavesCurrentBalanceNull()
    {
        var subAccount = ProvisionedSubAccount("client-1");
        subAccounts
            .Setup(x => x.ListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([subAccount]);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(subAccount.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityRegistration?)null);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundAccount?)null);

        var results = await handler.HandleAsync(AllTenantsQuery(), TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        results[0].CurrentBalance.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_AsAdmin_AuditsAllTenantReadAndSavesOnce()
    {
        subAccounts
            .Setup(x => x.ListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await handler.HandleAsync(AllTenantsQuery(), TestContext.Current.CancellationToken);

        auditLog.Verify(
            x => x.AppendAsync(
                "SubAccountsListed", "SubAccount", "*", It.IsAny<string>(),
                "apiso-admin", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
