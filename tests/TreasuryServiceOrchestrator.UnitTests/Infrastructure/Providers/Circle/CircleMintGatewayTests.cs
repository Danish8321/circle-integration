using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
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

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
