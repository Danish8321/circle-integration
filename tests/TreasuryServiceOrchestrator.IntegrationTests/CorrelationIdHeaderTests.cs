using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class CorrelationIdHeaderTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private const string HeaderName = "X-Correlation-Id";

    private async Task SeedSubAccountAsync(string clientCompanyId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        dbContext.SubAccounts.Add(subAccount);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SuccessfulResponse_HasCorrelationIdHeader()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        await SeedSubAccountAsync(clientCompanyId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);

        var response = await client.GetAsync(
            $"v1/sub-accounts/{clientCompanyId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(HeaderName));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues(HeaderName).First()));
    }

    [Fact]
    public async Task ErrorResponse_HasCorrelationIdHeader()
    {
        using var client = factory.CreateClient();
        // No ClientCompanyId header -> CallerIdentityMiddleware rejects with 401.

        var response = await client.GetAsync(
            "v1/sub-accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains(HeaderName));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues(HeaderName).First()));
    }
}
