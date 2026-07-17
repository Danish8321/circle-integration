using FluentAssertions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

/// <summary>
/// <see cref="IStablecoinGateway"/> has no members as of ticket 02 — ticket 01 shipped no Ledger
/// money-moving use cases, so there is nothing to mock yet (docs/features/02-mock-mode.md §3.4,
/// PHASE1_IMPLEMENTATION_PLAN.md 02.5: "this task stubs the type, later tickets extend it").
/// This test only pins that the stub type exists and satisfies the port; tickets 03/05/06/07 add
/// real coverage (RedeemAsync, GetTransferStatusAsync, failure injection, etc.) alongside their
/// own <see cref="IStablecoinGateway"/> method additions.
/// </summary>
public sealed class MockStablecoinGatewayTests
{
    [Fact]
    public void MockStablecoinGateway_ImplementsIStablecoinGateway()
    {
        var sut = new MockStablecoinGateway();

        sut.Should().BeAssignableTo<IStablecoinGateway>();
    }
}
