using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class GetCurrentBalanceQueryHandlerTests
{
    private readonly Mock<IFundAccountRepository> fundAccounts = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetCurrentBalanceQueryHandler handler;

    public GetCurrentBalanceQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetCurrentBalanceQueryHandler(
            fundAccounts.Object, callerContext.Object, TimeProvider.System);
    }

    [Fact]
    public async Task HandleAsync_WithExistingFundAccount_ReturnsItsBalance()
    {
        var fundAccount = FundAccount.Create("client-1", new Money(42m, "USDC"), DateTime.UtcNow);
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fundAccount);

        var result = await handler.HandleAsync(
            new GetCurrentBalanceQuery(), TestContext.Current.CancellationToken);

        result.Balance.Should().Be(new Money(42m, "USDC"));
        result.ClientCompanyId.Should().Be("client-1");
    }

    [Fact]
    public async Task HandleAsync_WithNoFundAccount_ReturnsUsdcZeroDefault()
    {
        // Ratified 2026-07-17: no FundAccount record yet must default to Money.Zero("USDC"),
        // never throw or return null — every funded account in this product is USDC.
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundAccount?)null);

        var result = await handler.HandleAsync(
            new GetCurrentBalanceQuery(), TestContext.Current.CancellationToken);

        result.Balance.Should().Be(Money.Zero("USDC"));
        result.Balance.CurrencyCode.Should().Be("USDC");
        result.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task HandleAsync_WithUnidentifiedCaller_ThrowsTenantForbiddenWithoutQueryingRepository()
    {
        callerContext.Setup(x => x.CallerId).Returns(string.Empty);

        var act = () => handler.HandleAsync(
            new GetCurrentBalanceQuery(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        fundAccounts.Verify(
            x => x.FindByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
