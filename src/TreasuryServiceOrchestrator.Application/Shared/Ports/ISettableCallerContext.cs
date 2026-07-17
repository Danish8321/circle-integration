namespace TreasuryServiceOrchestrator.Application.Shared.Ports;

/// <summary>
/// Lets a server-to-server caller (e.g. a webhook topic processor, authenticated by SNS
/// signature verification rather than the <c>ClientCompanyId</c> header) establish the
/// tenant identity that mutating handlers read from <see cref="ICallerContext"/>. The Api
/// tier's <c>HttpCallerContext</c> implements both interfaces; only trusted Infrastructure
/// callers (never Application use-case code itself) should depend on this port.
/// </summary>
public interface ISettableCallerContext : ICallerContext
{
    void Set(string callerId, CallerRole role);
}
