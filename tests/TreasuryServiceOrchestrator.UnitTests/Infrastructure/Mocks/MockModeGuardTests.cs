using FluentAssertions;
using Microsoft.Extensions.Hosting;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public sealed class MockModeGuardTests
{
    [Fact]
    public void Validate_MockModeEnabledInProduction_Throws()
    {
        var act = () => MockModeGuard.Validate(mockModeEnabled: true, environmentName: Environments.Production);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_MockModeEnabledInDevelopment_DoesNotThrow()
    {
        var act = () => MockModeGuard.Validate(mockModeEnabled: true, environmentName: Environments.Development);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MockModeDisabledInProduction_DoesNotThrow()
    {
        var act = () => MockModeGuard.Validate(mockModeEnabled: false, environmentName: Environments.Production);

        act.Should().NotThrow();
    }
}
