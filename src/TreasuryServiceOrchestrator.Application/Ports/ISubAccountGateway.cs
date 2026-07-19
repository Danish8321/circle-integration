
namespace TreasuryServiceOrchestrator.Application.Ports;

public interface ISubAccountGateway
{
    Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken = default);

    Task<CreateExternalEntityResult> GetExternalEntityAsync(
        string walletId, CancellationToken cancellationToken = default);
}
