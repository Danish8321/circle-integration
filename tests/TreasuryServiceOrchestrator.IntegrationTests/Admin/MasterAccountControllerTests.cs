using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Admin;

namespace TreasuryServiceOrchestrator.IntegrationTests.Admin;

public sealed class MasterAccountControllerTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "apiso-admin");
        return client;
    }

    [Fact]
    public async Task Summary_AsAdmin_ReturnsOkWithMainWalletTotalAndSubAccountCount()
    {
        using var client = CreateAdminClient();

        var response = await client.GetAsync(
            "v1/admin/master-account/summary", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GetMasterAccountSummaryResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("USDC", body!.MainWalletBalance.CurrencyCode);
        Assert.True(body.SubAccountCount >= 0);
    }

    [Fact]
    public async Task Summary_AsNonAdminCaller_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        dbContext.SubAccounts.Add(SubAccount.Create(clientCompanyId, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);

        var response = await client.GetAsync(
            "v1/admin/master-account/summary", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
