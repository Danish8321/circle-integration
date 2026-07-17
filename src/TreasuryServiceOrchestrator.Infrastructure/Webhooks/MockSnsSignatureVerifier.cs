using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

// Phase 1 stand-in: always accepts. Real AWS SNS cert-domain + SHA1/SHA256 verification
// (docs/features/03-webhook-processing.md §3.4) is Phase 3 scope.
public sealed class MockSnsSignatureVerifier : ISnsSignatureVerifier
{
    public Task<bool> VerifyAsync(SnsEnvelope envelope, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
