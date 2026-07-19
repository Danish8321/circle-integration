using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Api.Ledger;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class RecipientsEndpointsTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private WebApplicationFactory<Program> WithMockMode() => factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["MockMode:Enabled"] = "true",
                ["MockMode:WebhookDelayMilliseconds"] = "0",
            });
        });
    });

    private static HttpClient CreateClientFor(WebApplicationFactory<Program> app, string clientCompanyId)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        return client;
    }

    private static async Task<Guid> SeedSubAccountAsync(WebApplicationFactory<Program> app, string clientCompanyId)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        dbContext.SubAccounts.Add(subAccount);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return subAccount.Id;
    }

    [Fact]
    public async Task RegisterListGetRecipient_RoundTrip_ReturnsSameRecipient()
    {
        using var app = WithMockMode();
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var subAccountId = await SeedSubAccountAsync(app, clientCompanyId);
        using var client = CreateClientFor(app, clientCompanyId);

        var registerResponse = await client.PostAsJsonAsync(
            $"v1/sub-accounts/{subAccountId}/recipients",
            new RegisterRecipientRequest("ETH", "0xabc123", "Vendor A"),
            TestContext.Current.CancellationToken);

        Assert.True(
            registerResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"expected 200/201, got {registerResponse.StatusCode}");
        var registered = await registerResponse.Content.ReadFromJsonAsync<RecipientResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(registered);
        Assert.Equal(subAccountId, registered!.SubAccountId);
        Assert.Equal("ETH", registered.Chain);
        Assert.Equal("0xabc123", registered.Address);
        Assert.Equal("Vendor A", registered.Label);
        Assert.Equal(RecipientStatus.PendingApproval, registered.Status);

        var listResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/recipients", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await listResponse.Content.ReadFromJsonAsync<List<RecipientResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(listed);
        Assert.Single(listed!);
        Assert.Equal(registered.Id, listed![0].Id);

        var getResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/recipients/{registered.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<RecipientResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(fetched);
        Assert.Equal(registered.Id, fetched!.Id);
    }

    [Fact]
    public async Task RegisterRecipient_AfterMockWebhookDispatch_TransitionsToActive()
    {
        using var app = WithMockMode();
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var subAccountId = await SeedSubAccountAsync(app, clientCompanyId);
        using var client = CreateClientFor(app, clientCompanyId);

        var registerResponse = await client.PostAsJsonAsync(
            $"v1/sub-accounts/{subAccountId}/recipients",
            new RegisterRecipientRequest("ETH", "0xdef456", "Vendor B"),
            TestContext.Current.CancellationToken);
        Assert.True(
            registerResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"expected 200/201, got {registerResponse.StatusCode}");
        var registered = await registerResponse.Content.ReadFromJsonAsync<RecipientResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(registered);
        Assert.Equal(RecipientStatus.PendingApproval, registered!.Status);

        // Drive the mock addressBookRecipients webhook (scheduled by MockStablecoinGateway during
        // registration) through the same pipeline a real Circle SNS delivery uses.
        using (var scope = app.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<MockWebhookDispatcher>();
            await dispatcher.DispatchDueAsync(TestContext.Current.CancellationToken);
        }

        var getResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/recipients/{registered.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<RecipientResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(fetched);
        // Mock label "Vendor B" doesn't end with the reject suffix, so MockStablecoinGateway
        // schedules an "active" decision.
        Assert.Equal(RecipientStatus.Active, fetched!.Status);
    }
}
