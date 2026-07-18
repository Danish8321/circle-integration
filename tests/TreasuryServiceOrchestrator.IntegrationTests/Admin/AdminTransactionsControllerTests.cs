using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Api.Admin;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests.Admin;

public sealed class AdminTransactionsControllerTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private async Task<(Guid SubAccountId, string ClientCompanyId)> SeedTransactionAsync(
        TransactionType type = TransactionType.Deposit,
        TransactionStatus status = TransactionStatus.Complete)
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        dbContext.SubAccounts.Add(subAccount);

        var transaction = Transaction.Create(
            subAccount.Id,
            clientCompanyId,
            type,
            status,
            new Money(100m, "USDC"),
            $"ref-{Guid.NewGuid():N}",
            depositSourceType: null,
            failureReason: null,
            correlationId: $"corr-{Guid.NewGuid():N}",
            nowUtc: DateTime.UtcNow);
        dbContext.Transactions.Add(transaction);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (subAccount.Id, clientCompanyId);
    }

    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "apiso-admin");
        return client;
    }

    [Fact]
    public async Task ListAllTransactions_AsAdminUnfiltered_ReturnsAllTenantsTransactions()
    {
        var (_, clientCompanyIdA) = await SeedTransactionAsync();
        var (_, clientCompanyIdB) = await SeedTransactionAsync();

        using var client = CreateAdminClient();

        var response = await client.GetAsync("v1/admin/transactions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<AdminTransactionResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Contains(body!, t => string.Equals(t.ClientCompanyId, clientCompanyIdA, StringComparison.Ordinal));
        Assert.Contains(body!, t => string.Equals(t.ClientCompanyId, clientCompanyIdB, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListAllTransactions_AsAdminFilteredByClientCompanyId_ReturnsOnlyMatchingTenant()
    {
        var (_, clientCompanyIdA) = await SeedTransactionAsync();
        var (_, clientCompanyIdB) = await SeedTransactionAsync();

        using var client = CreateAdminClient();

        var response = await client.GetAsync(
            $"v1/admin/transactions?clientCompanyId={clientCompanyIdA}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<AdminTransactionResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.All(body!, t => Assert.Equal(clientCompanyIdA, t.ClientCompanyId));
        Assert.DoesNotContain(body!, t => string.Equals(t.ClientCompanyId, clientCompanyIdB, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListAllTransactions_AsNonAdminCaller_ReturnsForbiddenProblemDetails()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        dbContext.SubAccounts.Add(SubAccount.Create(clientCompanyId, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);

        var response = await client.GetAsync("v1/admin/transactions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
