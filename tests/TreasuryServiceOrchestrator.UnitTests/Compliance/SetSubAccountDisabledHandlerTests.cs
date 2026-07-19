using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Compliance;

public sealed class SetSubAccountDisabledHandlerTests
{
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IAuditLogService> auditLog = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly SetSubAccountDisabledHandler handler;

    public SetSubAccountDisabledHandlerTests()
    {
        callerContext.SetupGet(x => x.CallerId).Returns("apiso-admin");
        callerContext.SetupGet(x => x.Role).Returns(CallerRole.Admin);
        callerContext.SetupGet(x => x.IsAdmin).Returns(true);
        handler = new SetSubAccountDisabledHandler(
            subAccounts.Object, auditLog.Object, unitOfWork.Object, callerContext.Object);
    }

    private static SetSubAccountDisabledCommand Command(bool disabled) =>
        new("client-1", disabled, "corr-1");

    private static SubAccount ExistingSubAccount()
    {
        var subAccount = SubAccount.Create("client-1", new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc));
        subAccount.BeginCompliance("wallet-client-1");
        return subAccount;
    }

    [Fact]
    public async Task HandleAsync_AsNonAdminCaller_ThrowsTenantForbiddenAndDoesNotSave()
    {
        callerContext.SetupGet(x => x.CallerId).Returns("client-1");
        callerContext.SetupGet(x => x.Role).Returns(CallerRole.SubAccount);
        callerContext.SetupGet(x => x.IsAdmin).Returns(false);

        var act = () => handler.HandleAsync(Command(disabled: true), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        subAccounts.Verify(
            x => x.GetByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenSubAccountDoesNotExist_ThrowsNotFoundAndDoesNotSave()
    {
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);

        var act = () => handler.HandleAsync(Command(disabled: true), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AsAdmin_DisablesAuditsOnceAndSavesOnce()
    {
        var subAccount = ExistingSubAccount();
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);

        var result = await handler.HandleAsync(Command(disabled: true), TestContext.Current.CancellationToken);

        subAccount.IsDisabled.Should().BeTrue();
        result.SubAccountId.Should().Be(subAccount.Id);
        result.ClientCompanyId.Should().Be("client-1");
        result.IsDisabled.Should().BeTrue();
        auditLog.Verify(
            x => x.AppendAsync(
                "SubAccountDisabledSet", "SubAccount", subAccount.Id.ToString(), It.IsAny<string>(),
                "apiso-admin", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
        auditLog.VerifyNoOtherCalls();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyDisabled_SettingDisabledAgainStillSucceeds()
    {
        var subAccount = ExistingSubAccount();
        subAccount.SetDisabled(true);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);

        var result = await handler.HandleAsync(Command(disabled: true), TestContext.Current.CancellationToken);

        subAccount.IsDisabled.Should().BeTrue();
        result.IsDisabled.Should().BeTrue();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AsAdmin_ReEnablesDisabledSubAccount()
    {
        var subAccount = ExistingSubAccount();
        subAccount.SetDisabled(true);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);

        var result = await handler.HandleAsync(Command(disabled: false), TestContext.Current.CancellationToken);

        subAccount.IsDisabled.Should().BeFalse();
        result.IsDisabled.Should().BeFalse();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
