namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface ISnsSignatureVerifier
{
    Task<bool> VerifyAsync(SnsEnvelope envelope, CancellationToken cancellationToken = default);
}
