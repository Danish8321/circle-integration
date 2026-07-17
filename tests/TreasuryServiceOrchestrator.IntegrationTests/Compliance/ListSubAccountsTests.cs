using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.Api.Compliance;

namespace TreasuryServiceOrchestrator.IntegrationTests.Compliance;

public sealed class ListSubAccountsTests(TreasuryServiceOrchestratorApiFactory factory)
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
    public async Task ListSubAccounts_AsAdmin_ReturnsCreatedAccounts()
    {
        var first = await CreateSubAccountAsAdminAsync();
        var second = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor("apiso-admin");

        var response = await client.GetAsync("v1/sub-accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<SubAccountResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Contains(body!, x => string.Equals(x.ClientCompanyId, first, StringComparison.Ordinal));
        Assert.Contains(body!, x => string.Equals(x.ClientCompanyId, second, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListSubAccounts_WithPendingComplianceFilter_IncludesCreatedAccounts()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor("apiso-admin");

        var response = await client.GetAsync(
            "v1/sub-accounts?state=PendingCompliance", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<SubAccountResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Contains(body!, x => string.Equals(x.ClientCompanyId, clientCompanyId, StringComparison.Ordinal));
        Assert.All(body!, x => Assert.Equal("PendingCompliance", x.LifecycleState));
    }

    [Fact]
    public async Task ListSubAccounts_WithActiveFilter_ExcludesPendingComplianceAccounts()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor("apiso-admin");

        var response = await client.GetAsync(
            "v1/sub-accounts?state=Active", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<SubAccountResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.DoesNotContain(body!, x => string.Equals(x.ClientCompanyId, clientCompanyId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListSubAccounts_WithUnknownStateFilter_ReturnsBadRequestProblemDetails()
    {
        using var client = CreateClientFor("apiso-admin");

        var response = await client.GetAsync(
            "v1/sub-accounts?state=bogus", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ListSubAccounts_AsSubAccountCaller_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor(clientCompanyId);

        var response = await client.GetAsync("v1/sub-accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
