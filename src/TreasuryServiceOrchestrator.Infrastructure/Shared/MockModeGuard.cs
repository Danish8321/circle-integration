using Microsoft.Extensions.Hosting;

namespace TreasuryServiceOrchestrator.Infrastructure.Shared;

/// <summary>
/// Structural safety guard (CLAUDE.md invariant 9): mock mode must be structurally impossible
/// to enable in Production — a hard environment check, not config alone.
/// </summary>
public static class MockModeGuard
{
    public static void Validate(bool mockModeEnabled, string environmentName)
    {
        if (mockModeEnabled && string.Equals(environmentName, Environments.Production, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Mock mode cannot be enabled when ASPNETCORE_ENVIRONMENT is Production.");
        }
    }
}
