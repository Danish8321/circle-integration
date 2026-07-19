using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Providers.Circle;

/// <summary>
/// Ticket 03.5: <see cref="CircleMintGateway.GenerateDepositAddressAsync"/> against a fixture
/// <see cref="HttpMessageHandler"/> — no live Circle sandbox call (docs/features/09-deposits-and-funding.md
/// §7).
/// </summary>
public sealed class CircleMintGatewayTests
{
    [Fact]
    public async Task GenerateDepositAddressAsync_SendsIdempotencyKeyBodyAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            var envelope = new GeneratedDepositAddressCircleEnvelope
            {
                Data = new GeneratedDepositAddressCircleData
                {
                    Address = "0xabc123",
                    Chain = "ETH",
                    Currency = "USDC",
                },
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(envelope),
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var sut = new CircleMintGateway(httpClient);

        var request = new GenerateDepositAddressGatewayRequest(
            Chain: "ETH", Currency: "USDC", IdempotencyKey: "deposit-address:sub-1:ETH:USDC");

        var result = await sut.GenerateDepositAddressAsync(request, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/v1/businessAccount/wallets/addresses/deposit");
        capturedBody.Should().Contain("\"idempotencyKey\":\"deposit-address:sub-1:ETH:USDC\"");
        capturedBody.Should().Contain("\"currency\":\"USDC\"");
        capturedBody.Should().Contain("\"chain\":\"ETH\"");

        result.Address.Should().Be("0xabc123");
        result.Chain.Should().Be("ETH");
        result.Currency.Should().Be("USDC");
    }

    [Fact]
    public async Task RegisterRecipientAsync_SendsIdempotencyKeyBodyAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            var envelope = new RegisterRecipientCircleEnvelope
            {
                Data = new RegisterRecipientCircleData
                {
                    Id = "circle-recipient-1",
                    Status = "active",
                },
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(envelope),
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var sut = new CircleMintGateway(httpClient);

        var request = new RegisterRecipientGatewayRequest(
            Chain: "ETH", Address: "0xabc123", Label: "Primary payout wallet", IdempotencyKey: "recipient:sub-1:0xabc123");

        var result = await sut.RegisterRecipientAsync(request, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/v1/businessAccount/wallets/addresses/recipient");
        capturedBody.Should().Contain("\"idempotencyKey\":\"recipient:sub-1:0xabc123\"");
        capturedBody.Should().Contain("\"address\":\"0xabc123\"");
        capturedBody.Should().Contain("\"chain\":\"ETH\"");
        capturedBody.Should().Contain("\"description\":\"Primary payout wallet\"");

        result.CircleRecipientId.Should().Be("circle-recipient-1");
        result.Status.Should().Be("active");
    }

    [Fact]
    public async Task CreateTransferAsync_SendsIdempotencyKeyDestinationAmountAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            var envelope = new CreateTransferCircleEnvelope
            {
                Data = new CreateTransferCircleData
                {
                    Id = "circle-transfer-1",
                    Status = "pending",
                },
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(envelope),
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var sut = new CircleMintGateway(httpClient);

        var request = new CreateTransferGatewayRequest(
            DestinationRecipientId: "circle-recipient-1",
            Amount: new Money(100m, "USDC"),
            IdempotencyKey: "transfer:sub-1:1");

        var result = await sut.CreateTransferAsync(request, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/v1/businessAccount/transfers");
        capturedBody.Should().Contain("\"idempotencyKey\":\"transfer:sub-1:1\"");
        capturedBody.Should().Contain("\"type\":\"verified_blockchain\"");
        capturedBody.Should().Contain("\"addressId\":\"circle-recipient-1\"");
        capturedBody.Should().Contain("\"amount\":\"100\"");
        capturedBody.Should().Contain("\"currency\":\"USDC\"");
        capturedBody.Should().NotContain("identities");
        capturedBody.Should().NotContain("originator");

        result.CircleTransferId.Should().Be("circle-transfer-1");
        result.Status.Should().Be("pending");
    }

    [Fact]
    public async Task RedeemAsync_AlwaysSendsSourceExplicitlyAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            var envelope = new RedeemCircleEnvelope
            {
                Data = new RedeemCircleData
                {
                    Id = "redeem-1",
                    Status = "pending",
                },
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(envelope),
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var sut = new CircleMintGateway(httpClient);

        var request = new RedeemGatewayRequest(
            IdempotencyKey: "redeem:sub-1:1",
            SourceWalletId: "wallet-1",
            DestinationBankAccountId: "bank-account-1",
            GrossAmount: new Money(500m, "USD"));

        var result = await sut.RedeemAsync(request, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/v1/businessAccount/payouts");
        capturedBody.Should().Contain("\"idempotencyKey\":\"redeem:sub-1:1\"");
        capturedBody.Should().Contain("\"source\":{\"type\":\"wallet\",\"id\":\"wallet-1\"}");
        capturedBody.Should().Contain("\"destination\":{\"type\":\"wire\",\"id\":\"bank-account-1\"}");

        result.CircleRedeemId.Should().Be("redeem-1");
        result.Status.Should().Be("pending");
    }

    [Fact]
    public async Task CreateLinkedBankAccountAsync_SendsUsWireCreationBodyAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            var envelope = new CreateLinkedBankAccountCircleEnvelope
            {
                Data = new CreateLinkedBankAccountCircleData
                {
                    Id = "bank-account-1",
                    Status = "pending",
                },
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(envelope),
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var sut = new CircleMintGateway(httpClient);

        var request = new CreateLinkedBankAccountGatewayRequest(
            IdempotencyKey: "linked-bank-account:1",
            BeneficiaryName: "Acme Corp",
            AccountNumber: "123456789",
            RoutingNumber: "021000021",
            BankName: "Test Bank",
            BillingName: "Acme Corp",
            BillingCity: "New York",
            BillingCountry: "US",
            BillingLine1: "123 Main St",
            BillingPostalCode: "10001",
            BillingLine2: null,
            BillingDistrict: null,
            BankAddressCountry: "US",
            BankAddressBankName: "Test Bank");

        var result = await sut.CreateLinkedBankAccountAsync(request, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/v1/businessAccount/banks/wires");
        capturedBody.Should().Contain("\"idempotencyKey\":\"linked-bank-account:1\"");
        capturedBody.Should().Contain("\"accountNumber\":\"123456789\"");
        capturedBody.Should().Contain("\"routingNumber\":\"021000021\"");
        capturedBody.Should().Contain("\"billingDetails\":{\"name\":\"Acme Corp\",\"city\":\"New York\",\"country\":\"US\",\"line1\":\"123 Main St\",\"postalCode\":\"10001\"");
        capturedBody.Should().Contain("\"bankAddress\":{\"country\":\"US\",\"bankName\":\"Test Bank\"}");

        result.CircleBankAccountId.Should().Be("bank-account-1");
        result.Status.Should().Be("pending");
    }

    [Fact]
    public async Task GetWireInstructionsAsync_SendsExpectedPathAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler((request, ct) =>
        {
            capturedRequest = request;

            var envelope = new WireInstructionsCircleEnvelope
            {
                Data = new WireInstructionsCircleData
                {
                    TrackingRef = "TRACK123",
                    Beneficiary = new WireInstructionsCircleBeneficiary
                    {
                        Name = "Circle Internet Financial",
                        Address = "1 Circle Way",
                    },
                    BeneficiaryBank = new WireInstructionsCircleBeneficiaryBank
                    {
                        Name = "Signature Bank",
                        SwiftCode = "SIGNUS33",
                        RoutingNumber = "026013356",
                        AccountNumber = "****5678",
                        Currency = "USD",
                    },
                },
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(envelope),
            });
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var sut = new CircleMintGateway(httpClient);

        var result = await sut.GetWireInstructionsAsync("bank-account-1", TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/v1/businessAccount/banks/wires/bank-account-1/instructions");

        result.TrackingRef.Should().Be("TRACK123");
        result.BeneficiaryName.Should().Be("Circle Internet Financial");
        result.BeneficiaryAddress.Should().Be("1 Circle Way");
        result.BankName.Should().Be("Signature Bank");
        result.SwiftCode.Should().Be("SIGNUS33");
        result.RoutingNumber.Should().Be("026013356");
        result.MaskedAccountNumber.Should().Be("****5678");
        result.Currency.Should().Be("USD");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
