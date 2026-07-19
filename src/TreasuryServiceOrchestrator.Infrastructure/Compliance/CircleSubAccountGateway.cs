using System.Net.Http.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Compliance;

public sealed class CircleSubAccountGateway(HttpClient httpClient) : ISubAccountGateway
{
    public async Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken = default)
    {
        var circleRequest = new CreateExternalEntityCircleRequest
        {
            BusinessName = request.BusinessName,
            BusinessUniqueIdentifier = request.BusinessUniqueIdentifier,
            IdentifierIssuingCountryCode = request.IdentifierIssuingCountryCode,
            Address = new CircleExternalEntityAddress
            {
                Country = request.Country,
                State = request.State,
                City = request.City,
                Postcode = request.Postcode,
                StreetName = request.StreetName,
                BuildingNumber = request.BuildingNumber,
            },
        };

        using var response = await httpClient.PostAsJsonAsync("v1/externalEntities", circleRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<ExternalEntityCircleEnvelope>(cancellationToken)
            ?? throw new InvalidOperationException("Circle returned an empty externalEntities response.");

        return new CreateExternalEntityResult(
            envelope.Data.WalletId, envelope.Data.ComplianceState, envelope.Data.BusinessName,
            envelope.Data.BusinessUniqueIdentifier);
    }

    public async Task<CreateExternalEntityResult> GetExternalEntityAsync(
        string walletId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"v1/externalEntities/{walletId}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<ExternalEntityCircleEnvelope>(cancellationToken)
            ?? throw new InvalidOperationException("Circle returned an empty externalEntities response.");

        return new CreateExternalEntityResult(
            envelope.Data.WalletId, envelope.Data.ComplianceState, envelope.Data.BusinessName,
            envelope.Data.BusinessUniqueIdentifier);
    }
}
