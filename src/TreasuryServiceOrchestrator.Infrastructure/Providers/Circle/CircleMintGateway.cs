using System.Globalization;
using System.Net.Http.Json;

using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

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

    public async Task<CreatedTransfer> CreateTransferAsync(
        CreateTransferGatewayRequest request, CancellationToken ct = default)
    {
        var circleRequest = new CreateTransferCircleRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            Destination = new CreateTransferCircleDestination
            {
                AddressId = request.DestinationRecipientId,
            },
            Amount = new CreateTransferCircleAmount
            {
                Amount = request.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                Currency = request.Amount.CurrencyCode,
            },
        };

        using var response = await httpClient.PostAsJsonAsync(
            "v1/businessAccount/transfers", circleRequest, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<CreateTransferCircleEnvelope>(ct)
            ?? throw new InvalidOperationException("Circle returned an empty transfer-creation response.");

        return new CreatedTransfer(envelope.Data.Id, envelope.Data.Status ?? string.Empty);
    }

    public async Task<CreatedRedeem> RedeemAsync(
        RedeemGatewayRequest request, CancellationToken ct = default)
    {
        // docs/features/11-redemption-and-payouts.md §7 — `source` is always set explicitly,
        // never omitted (CLAUDE.md invariant 12 hazard family: an omitted source silently
        // debits the Distributor's Master Account wallet instead of the sub-account's).
        var circleRequest = new RedeemCircleRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            Source = new RedeemCircleSource { Id = request.SourceWalletId },
            Destination = new RedeemCircleDestination { Id = request.DestinationBankAccountId },
        };

        using var response = await httpClient.PostAsJsonAsync(
            "v1/businessAccount/payouts", circleRequest, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<RedeemCircleEnvelope>(ct)
            ?? throw new InvalidOperationException("Circle returned an empty redeem response.");

        return new CreatedRedeem(envelope.Data.Id, envelope.Data.Status ?? string.Empty);
    }

    public async Task<CreatedLinkedBankAccount> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken ct = default)
    {
        var circleRequest = new CreateLinkedBankAccountCircleRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            AccountNumber = request.AccountNumber,
            RoutingNumber = request.RoutingNumber,
            BillingDetails = new CreateLinkedBankAccountCircleBillingDetails
            {
                Name = request.BillingName,
                City = request.BillingCity,
                Country = request.BillingCountry,
                Line1 = request.BillingLine1,
                PostalCode = request.BillingPostalCode,
                Line2 = request.BillingLine2,
                District = request.BillingDistrict,
            },
            BankAddress = new CreateLinkedBankAccountCircleBankAddress
            {
                Country = request.BankAddressCountry,
                BankName = request.BankAddressBankName,
            },
        };

        using var response = await httpClient.PostAsJsonAsync(
            "v1/businessAccount/banks/wires", circleRequest, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<CreateLinkedBankAccountCircleEnvelope>(ct)
            ?? throw new InvalidOperationException("Circle returned an empty wire bank account response.");

        return new CreatedLinkedBankAccount(envelope.Data.Id, envelope.Data.Status ?? string.Empty);
    }

    public async Task<WireInstructions> GetWireInstructionsAsync(
        string circleBankAccountId, CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync(
            $"v1/businessAccount/banks/wires/{circleBankAccountId}/instructions", ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<WireInstructionsCircleEnvelope>(ct)
            ?? throw new InvalidOperationException("Circle returned an empty wire instructions response.");

        return new WireInstructions(
            envelope.Data.TrackingRef,
            envelope.Data.Beneficiary.Name,
            envelope.Data.Beneficiary.Address,
            envelope.Data.BeneficiaryBank.Name,
            envelope.Data.BeneficiaryBank.SwiftCode,
            envelope.Data.BeneficiaryBank.RoutingNumber,
            envelope.Data.BeneficiaryBank.AccountNumber,
            envelope.Data.BeneficiaryBank.Currency);
    }

    public async Task<Money> GetMainWalletBalanceAsync(CancellationToken ct = default)
    {
        // docs/features/12-admin-cross-tenant-views.md §3 — walletId deliberately omitted:
        // Circle defaults an omitted walletId to the Distributor's own main wallet, the one
        // deliberate exception to every other walletId-scoped call in this gateway.
        using var response = await httpClient.GetAsync("v1/businessAccount/balances", ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<GetMainWalletBalanceCircleEnvelope>(ct)
            ?? throw new InvalidOperationException("Circle returned an empty balances response.");

        var amount = envelope.Data.Available.Count > 0
            ? decimal.Parse(envelope.Data.Available[0].Amount, CultureInfo.InvariantCulture)
            : 0m;

        return new Money(amount, "USDC");
    }

    public async Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken ct = default)
    {
        var records = new List<ProviderDepositRecord>();
        var since = sinceUtc.ToString("o", CultureInfo.InvariantCulture);

        using (var depositsResponse = await httpClient.GetAsync(
            $"v1/businessAccount/deposits?walletId={circleWalletId}&from={since}", ct))
        {
            depositsResponse.EnsureSuccessStatusCode();

            var depositsEnvelope = await depositsResponse.Content
                .ReadFromJsonAsync<ListDepositsCircleEnvelope>(ct)
                ?? throw new InvalidOperationException("Circle returned an empty deposits-list response.");

            records.AddRange(depositsEnvelope.Data.Select(deposit => new ProviderDepositRecord(
                deposit.Id,
                deposit.Destination.Id,
                DestinationAddress: deposit.Destination.Id,
                new Money(decimal.Parse(deposit.Amount.Amount, CultureInfo.InvariantCulture), deposit.Amount.Currency),
                DepositSourceType.Wire,
                deposit.CreateDate)));
        }

        using (var transfersResponse = await httpClient.GetAsync(
            $"v1/businessAccount/transfers?destinationWalletId={circleWalletId}&from={since}", ct))
        {
            transfersResponse.EnsureSuccessStatusCode();

            var transfersEnvelope = await transfersResponse.Content
                .ReadFromJsonAsync<ListTransfersCircleEnvelope>(ct)
                ?? throw new InvalidOperationException("Circle returned an empty transfers-list response.");

            records.AddRange(transfersEnvelope.Data.Select(transfer => new ProviderDepositRecord(
                transfer.Id,
                circleWalletId,
                DestinationAddress: transfer.Destination.Address ?? transfer.Destination.Id ?? string.Empty,
                new Money(decimal.Parse(transfer.Amount.Amount, CultureInfo.InvariantCulture), transfer.Amount.Currency),
                DepositSourceType.OnChain,
                transfer.CreateDate)));
        }

        return records;
    }
}
