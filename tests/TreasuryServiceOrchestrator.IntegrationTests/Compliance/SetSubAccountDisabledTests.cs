using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.Api.Compliance;

namespace TreasuryServiceOrchestrator.IntegrationTests.Compliance;

public sealed class SetSubAccountDisabledTests(TreasuryServiceOrchestratorApiFactory factory)
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

    private static Task<HttpResponseMessage> PutDisabledAsync(
        HttpClient client, string clientCompanyId, bool disabled) =>
        client.PutAsJsonAsync(
            $"v1/sub-accounts/{clientCompanyId}/disabled",
            new SetSubAccountDisabledRequest(disabled),
            TestContext.Current.CancellationToken);

    private async Task<bool> GetIsDisabledAsync(string clientCompanyId)
    {
        using var admin = CreateClientFor("apiso-admin");
        var response = await admin.GetAsync(
            $"v1/sub-accounts/{clientCompanyId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubAccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body!.IsDisabled;
    }

    [Fact]
    public async Task PutDisabled_AsAdmin_DisablesSubAccount()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var admin = CreateClientFor("apiso-admin");

        var response = await PutDisabledAsync(admin, clientCompanyId, disabled: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SetSubAccountDisabledResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(clientCompanyId, body!.ClientCompanyId);
        Assert.True(body.IsDisabled);
        Assert.True(await GetIsDisabledAsync(clientCompanyId));
    }

    [Fact]
    public async Task PutDisabled_RepeatedWithSameValue_IsIdempotent()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var admin = CreateClientFor("apiso-admin");

        var first = await PutDisabledAsync(admin, clientCompanyId, disabled: true);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PutDisabledAsync(admin, clientCompanyId, disabled: true);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<SetSubAccountDisabledResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.True(body!.IsDisabled);
        Assert.True(await GetIsDisabledAsync(clientCompanyId));
    }

    [Fact]
    public async Task PutDisabled_False_ReEnablesSubAccount()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var admin = CreateClientFor("apiso-admin");

        var disable = await PutDisabledAsync(admin, clientCompanyId, disabled: true);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var enable = await PutDisabledAsync(admin, clientCompanyId, disabled: false);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var body = await enable.Content.ReadFromJsonAsync<SetSubAccountDisabledResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.False(body!.IsDisabled);
        Assert.False(await GetIsDisabledAsync(clientCompanyId));
    }

    [Fact]
    public async Task PutDisabled_AsOwningSubAccountCaller_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor(clientCompanyId);

        var response = await PutDisabledAsync(client, clientCompanyId, disabled: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(await GetIsDisabledAsync(clientCompanyId));
    }

    [Fact]
    public async Task PutDisabled_AsAdminForNonexistentTenant_ReturnsNotFoundProblemDetails()
    {
        using var admin = CreateClientFor("apiso-admin");

        var response = await PutDisabledAsync(
            admin, $"no-such-client-{Guid.NewGuid():N}", disabled: true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
