using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Api.Ledger;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class DepositAddressesEndpointsTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private HttpClient CreateClientFor(string clientCompanyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        return client;
    }

    private async Task<Guid> SeedSubAccountAsync(string clientCompanyId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        dbContext.SubAccounts.Add(subAccount);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return subAccount.Id;
    }

    [Fact]
    public async Task GenerateDepositAddress_WithSupportedChain_ReturnsCreatedWithAddress()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var subAccountId = await SeedSubAccountAsync(clientCompanyId);
        using var client = CreateClientFor(clientCompanyId);

        var response = await client.PostAsJsonAsync(
            $"v1/sub-accounts/{subAccountId}/deposit-addresses",
            new GenerateDepositAddressRequest("ETH", "USDC"),
            TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"expected 200/201, got {response.StatusCode}");
        var body = await response.Content.ReadFromJsonAsync<DepositAddressResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(subAccountId, body!.SubAccountId);
        Assert.Equal("ETH", body.Chain);
        Assert.Equal("USDC", body.Currency);
        Assert.False(string.IsNullOrWhiteSpace(body.Address));
    }

    [Fact]
    public async Task ListDepositAddresses_AfterGenerating_ReturnsGeneratedAddress()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var subAccountId = await SeedSubAccountAsync(clientCompanyId);
        using var client = CreateClientFor(clientCompanyId);

        var generateResponse = await client.PostAsJsonAsync(
            $"v1/sub-accounts/{subAccountId}/deposit-addresses",
            new GenerateDepositAddressRequest("ETH", "USDC"),
            TestContext.Current.CancellationToken);
        Assert.True(
            generateResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"expected 200/201, got {generateResponse.StatusCode}");

        var listResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/deposit-addresses", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var body = await listResponse.Content.ReadFromJsonAsync<List<DepositAddressResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Single(body!);
        Assert.Equal(subAccountId, body![0].SubAccountId);
        Assert.Equal("ETH", body[0].Chain);
    }

    [Fact]
    public async Task GenerateDepositAddress_WithUnsupportedChain_ReturnsBadRequestProblemDetails()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var subAccountId = await SeedSubAccountAsync(clientCompanyId);
        using var client = CreateClientFor(clientCompanyId);

        var response = await client.PostAsJsonAsync(
            $"v1/sub-accounts/{subAccountId}/deposit-addresses",
            new GenerateDepositAddressRequest("SOL", "USDC"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
