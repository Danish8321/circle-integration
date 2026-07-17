using System.Net.Http.Json;

using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

/// <summary>
/// Real Circle Mint HTTP gateway for <see cref="IStablecoinGateway"/> (docs/features/09-deposits-and-funding.md
/// §7). Ticket 03.5 implements <see cref="GenerateDepositAddressAsync"/> only; later tickets
/// (05/06/07) extend it alongside their own <see cref="IStablecoinGateway"/> additions.
/// </summary>
public sealed class CircleMintGateway(HttpClient httpClient) : IStablecoinGateway
{
    public async Task<GeneratedDepositAddress> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken ct = default)
    {
        var circleRequest = new GenerateDepositAddressCircleRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            Currency = request.Currency,
            Chain = request.Chain,
        };

        using var response = await httpClient.PostAsJsonAsync(
            "v1/businessAccount/wallets/addresses/deposit", circleRequest, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<GeneratedDepositAddressCircleEnvelope>(ct)
            ?? throw new InvalidOperationException("Circle returned an empty deposit-address response.");

        return new GeneratedDepositAddress(
            envelope.Data.Address, envelope.Data.Chain, envelope.Data.Currency, ProviderAddressId: null);
    }

    public async Task<RegisteredRecipient> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken ct = default)
    {
        var circleRequest = new RegisterRecipientCircleRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            Address = request.Address,
            Chain = request.Chain,
            Description = request.Label,
        };

        using var response = await httpClient.PostAsJsonAsync(
            "v1/businessAccount/wallets/addresses/recipient", circleRequest, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<RegisterRecipientCircleEnvelope>(ct)
            ?? throw new InvalidOperationException("Circle returned an empty recipient-registration response.");

        return new RegisteredRecipient(envelope.Data.Id, envelope.Data.Status ?? string.Empty);
    }
}
