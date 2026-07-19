namespace TreasuryServiceOrchestrator.Application.Ports;

public interface ISnsSignatureVerifier
{
    Task<bool> VerifyAsync(SnsEnvelope envelope, CancellationToken cancellationToken = default);
}
