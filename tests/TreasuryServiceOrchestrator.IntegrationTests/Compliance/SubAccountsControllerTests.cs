using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Api.Compliance;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests.Compliance;

public sealed class SubAccountsControllerTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private async Task SeedRegisteredCallerAsync(string clientCompanyId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        dbContext.SubAccounts.Add(SubAccount.Create(clientCompanyId, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

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

    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "apiso-admin");
        return client;
    }

    [Fact]
    public async Task CreateSubAccount_AsAdminWithIdempotencyKey_ReturnsCreatedWithPendingCompliance()
    {
        using var client = CreateAdminClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync(
            "v1/sub-accounts", ValidRequest($"client-{Guid.NewGuid():N}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateSubAccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("PendingCompliance", body!.LifecycleState);
        Assert.False(string.IsNullOrWhiteSpace(body.CircleWalletId));
    }

    [Fact]
    public async Task CreateSubAccount_WithoutClientCompanyIdHeader_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "v1/sub-accounts", ValidRequest($"client-{Guid.NewGuid():N}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubAccount_AsNonAdminCaller_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        await SeedRegisteredCallerAsync(clientCompanyId);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync(
            "v1/sub-accounts", ValidRequest($"client-{Guid.NewGuid():N}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateSubAccount_AsNonAdminCallerForOwnClientCompanyId_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        await SeedRegisteredCallerAsync(clientCompanyId);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync(
            "v1/sub-accounts", ValidRequest(clientCompanyId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateSubAccount_WithBlankBusinessName_ReturnsBadRequestProblemDetails()
    {
        using var client = CreateAdminClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");
        var invalidRequest = ValidRequest($"client-{Guid.NewGuid():N}") with { BusinessName = "" };

        var response = await client.PostAsJsonAsync(
            "v1/sub-accounts", invalidRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubAccount_CalledTwiceForSameClientCompany_ReturnsConflictProblemDetails()
    {
        using var client = CreateAdminClient();
        var clientCompanyId = $"client-{Guid.NewGuid():N}";

        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");
        var first = await client.PostAsJsonAsync(
            "v1/sub-accounts", ValidRequest(clientCompanyId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");
        var second = await client.PostAsJsonAsync(
            "v1/sub-accounts", ValidRequest(clientCompanyId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
