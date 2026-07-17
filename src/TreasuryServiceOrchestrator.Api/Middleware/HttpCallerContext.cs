using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class HttpCallerContext : ICallerContext
{
    public string CallerId { get; private set; } = string.Empty;
    public CallerRole Role { get; private set; }

    public void Set(string callerId, CallerRole role)
    {
        CallerId = callerId;
        Role = role;
    }
}
