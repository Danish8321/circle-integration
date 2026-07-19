namespace TreasuryServiceOrchestrator.Application.Ports;

public interface ICallerContext
{
    string CallerId { get; }
    CallerRole Role { get; }
    bool IsAdmin => Role == CallerRole.Admin;
}
