using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

/// <summary>
/// Deterministic, no-network stand-in for <see cref="CircleSubAccountGateway"/>, wired only in
/// Development so this slice runs end to end without a Circle sandbox account. Distinct from the
/// formal mock-provider system (Phase 1 Task 6, PRD §13) which this repo doesn't have yet.
/// </summary>
public sealed class FakeSubAccountGateway : ISubAccountGateway
{
    public Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken = default)
    {
        var result = new CreateExternalEntityResult(
            WalletId: $"dev-{Guid.NewGuid():N}",
            ComplianceState: "PENDING",
            BusinessName: request.BusinessName,
            BusinessUniqueIdentifier: request.BusinessUniqueIdentifier);

        return Task.FromResult(result);
    }
}
