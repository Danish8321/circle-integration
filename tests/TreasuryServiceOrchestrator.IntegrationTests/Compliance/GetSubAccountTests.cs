using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.Api.Compliance;

namespace TreasuryServiceOrchestrator.IntegrationTests.Compliance;

public sealed class GetSubAccountTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private static CreateSubAccountRequest ValidRequest(string clientCompanyId) => new(
        ClientCompanyId: clientCompanyId,
        BusinessName: "Acme Inc",
        BusinessUniqueIdentifier: "EIN-123",
        IdentifierIssuingCountryCode: "US",
        Country: "US",
        State: "NY",
        City: "New York",
        Postcode: "10001",
        StreetName: "Broadway",
        BuildingNumber: "1");

    private HttpClient CreateClientFor(string clientCompanyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        return client;
    }

    private async Task<string> CreateSubAccountAsAdminAsync()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        using var admin = CreateClientFor("apiso-admin");
        admin.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");

        var response = await admin.PostAsJsonAsync(
            "v1/sub-accounts", ValidRequest(clientCompanyId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return clientCompanyId;
    }

    [Fact]
    public async Task GetSubAccount_AsOwningSubAccountCaller_ReturnsDetails()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor(clientCompanyId);

        var response = await client.GetAsync(
            $"v1/sub-accounts/{clientCompanyId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubAccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(clientCompanyId, body!.ClientCompanyId);
        Assert.Equal("PendingCompliance", body.LifecycleState);
        Assert.False(body.IsDisabled);
        Assert.False(string.IsNullOrWhiteSpace(body.CircleWalletId));
        Assert.NotNull(body.LatestRegistrationStatus);
    }

    [Fact]
    public async Task GetSubAccount_AsOtherTenantCaller_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        var otherClientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor(otherClientCompanyId);

        var response = await client.GetAsync(
            $"v1/sub-accounts/{clientCompanyId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetSubAccount_AsAdminForExistingTenant_ReturnsDetails()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor("apiso-admin");

        var response = await client.GetAsync(
            $"v1/sub-accounts/{clientCompanyId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubAccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(clientCompanyId, body!.ClientCompanyId);
    }

    [Fact]
    public async Task GetSubAccount_AsAdminForNonexistentTenant_ReturnsNotFoundProblemDetails()
    {
        using var client = CreateClientFor("apiso-admin");

        var response = await client.GetAsync(
            $"v1/sub-accounts/no-such-client-{Guid.NewGuid():N}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
