using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Api.Compliance;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests.Compliance;

public sealed class ResubmitEntityRegistrationTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private static CreateSubAccountRequest ValidCreateRequest(string clientCompanyId) => new(
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

    private static ResubmitEntityRegistrationRequest ValidResubmitRequest() => new(
        BusinessName: "Acme Incorporated",
        BusinessUniqueIdentifier: "EIN-456",
        IdentifierIssuingCountryCode: "US",
        Country: "US",
        State: "NY",
        City: "New York",
        Postcode: "10002",
        StreetName: "Fifth Avenue",
        BuildingNumber: "5");

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
            "v1/sub-accounts", ValidCreateRequest(clientCompanyId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return clientCompanyId;
    }

    private async Task RejectRegistrationAsync(string clientCompanyId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var subAccount = await dbContext.SubAccounts.SingleAsync(
            x => x.ClientCompanyId == clientCompanyId, TestContext.Current.CancellationToken);
        subAccount.MarkRejected();

        var registration = await dbContext.EntityRegistrations
            .Where(x => x.SubAccountId == subAccount.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(TestContext.Current.CancellationToken);
        registration.Reject("Incomplete documents", DateTime.UtcNow);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PostResubmitAsync(HttpClient client, string clientCompanyId)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");
        return client.PostAsJsonAsync(
            $"v1/sub-accounts/{clientCompanyId}/registrations",
            ValidResubmitRequest(),
            TestContext.Current.CancellationToken);
    }

    private async Task<int> CountRegistrationsAsync(string clientCompanyId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        return await dbContext.EntityRegistrations
            .CountAsync(
                x => x.ClientCompanyId == clientCompanyId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Resubmit_AsOwningSubAccountCaller_ReturnsCreatedAndPendingCompliance()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        await RejectRegistrationAsync(clientCompanyId);
        using var client = CreateClientFor(clientCompanyId);

        var response = await PostResubmitAsync(client, clientCompanyId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResubmitEntityRegistrationResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(clientCompanyId, body!.ClientCompanyId);
        Assert.NotEqual(Guid.Empty, body.RegistrationId);
        Assert.Equal("PendingCompliance", body.LifecycleState);
        Assert.Equal("Pending", body.RegistrationStatus);

        using var admin = CreateClientFor("apiso-admin");
        var getResponse = await admin.GetAsync(
            $"v1/sub-accounts/{clientCompanyId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var subAccount = await getResponse.Content.ReadFromJsonAsync<SubAccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(subAccount);
        Assert.Equal("PendingCompliance", subAccount!.LifecycleState);
        Assert.Equal("Pending", subAccount.LatestRegistrationStatus);

        Assert.Equal(2, await CountRegistrationsAsync(clientCompanyId));
    }

    [Fact]
    public async Task Resubmit_WhenNotRejected_ReturnsConflictProblemDetails()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        using var client = CreateClientFor(clientCompanyId);

        var response = await PostResubmitAsync(client, clientCompanyId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, await CountRegistrationsAsync(clientCompanyId));
    }

    [Fact]
    public async Task Resubmit_AsOtherTenantCaller_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = await CreateSubAccountAsAdminAsync();
        await RejectRegistrationAsync(clientCompanyId);
        using var client = CreateClientFor("some-other-client");

        var response = await PostResubmitAsync(client, clientCompanyId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Resubmit_AsAdminForNonexistentTenant_ReturnsNotFoundProblemDetails()
    {
        using var admin = CreateClientFor("apiso-admin");

        var response = await PostResubmitAsync(admin, $"no-such-client-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
