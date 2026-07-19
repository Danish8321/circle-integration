using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class TransactionsAndBalancesEndpointsTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private HttpClient CreateClientFor(string clientCompanyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        return client;
    }

    private async Task<(Guid SubAccountId, string WalletId)> SeedActiveSubAccountAsync(string clientCompanyId)
    {
        var walletId = $"wallet-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        subAccount.BeginCompliance(walletId);
        dbContext.SubAccounts.Add(subAccount);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (subAccount.Id, walletId);
    }

    private static object DepositsSnsEnvelope(string messageId, string walletId, string depositId, string amount)
    {
        var innerMessage = JsonSerializer.Serialize(new
        {
            notificationType = "deposits",
            deposit = new
            {
                id = depositId,
                walletId,
                amount = new { amount, currency = "USD" },
            },
        });

        return new
        {
            Type = "Notification",
            MessageId = messageId,
            TopicArn = "arn:aws:sns:us-east-1:000000000000:test-topic",
            Message = innerMessage,
            Signature = "irrelevant-under-mock-verifier",
            SigningCertURL = "https://sns.us-east-1.amazonaws.com/cert.pem",
        };
    }

    [Fact]
    public async Task DepositsWebhook_Delivery_CreditsLedgerAndReflectsInEndpoints()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var (subAccountId, walletId) = await SeedActiveSubAccountAsync(clientCompanyId);
        var depositId = $"deposit-{Guid.NewGuid():N}";

        using var webhookClient = factory.CreateClient();
        var webhookResponse = await webhookClient.PostAsJsonAsync(
            "v1/webhooks/circle",
            DepositsSnsEnvelope($"msg-{Guid.NewGuid():N}", walletId, depositId, "100.00"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);

        using var client = CreateClientFor(clientCompanyId);

        var transactionsResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, transactionsResponse.StatusCode);
        var transactions = await transactionsResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(transactions);
        var transaction = Assert.Single(transactions!);
        Assert.Equal(TransactionType.Deposit, transaction.Type);
        Assert.Equal(TransactionStatus.Complete, transaction.Status);
        Assert.Equal(depositId, transaction.ProviderReferenceId);

        var transactionResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/transactions/{transaction.TransactionId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, transactionResponse.StatusCode);
        var singleTransaction = await transactionResponse.Content.ReadFromJsonAsync<TransactionResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(singleTransaction);
        Assert.Equal(transaction.TransactionId, singleTransaction!.TransactionId);

        var balanceResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/balances", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
        var balance = await balanceResponse.Content.ReadFromJsonAsync<BalanceResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(balance);
        Assert.Equal(100.00m, balance!.Balance.Amount);

        var historyResponse = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/balances/history", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<BalanceSnapshotResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(history);
        var snapshot = Assert.Single(history!);
        Assert.Equal(BalanceSnapshotReason.PostMutation, snapshot.Reason);
    }

    [Fact]
    public async Task GetCurrentBalance_NoDepositsYet_ReturnsZeroUsdc()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var (subAccountId, _) = await SeedActiveSubAccountAsync(clientCompanyId);
        using var client = CreateClientFor(clientCompanyId);

        var response = await client.GetAsync(
            $"v1/sub-accounts/{subAccountId}/balances", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var balance = await response.Content.ReadFromJsonAsync<BalanceResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(balance);
        Assert.Equal(0m, balance!.Balance.Amount);
        Assert.Equal("USDC", balance.Balance.CurrencyCode);
    }

    [Fact]
    public async Task GetTransaction_BelongingToDifferentTenant_ReturnsNotFound()
    {
        var ownerClientCompanyId = $"client-{Guid.NewGuid():N}";
        var (subAccountId, walletId) = await SeedActiveSubAccountAsync(ownerClientCompanyId);
        var depositId = $"deposit-{Guid.NewGuid():N}";

        using var webhookClient = factory.CreateClient();
        await webhookClient.PostAsJsonAsync(
            "v1/webhooks/circle",
            DepositsSnsEnvelope($"msg-{Guid.NewGuid():N}", walletId, depositId, "50.00"),
            TestContext.Current.CancellationToken);

        using var ownerClient = CreateClientFor(ownerClientCompanyId);
        var listResponse = await ownerClient.GetAsync(
            $"v1/sub-accounts/{subAccountId}/transactions", TestContext.Current.CancellationToken);
        var transactions = await listResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>(
            TestContext.Current.CancellationToken);
        var transactionId = Assert.Single(transactions!).TransactionId;

        var otherClientCompanyId = $"client-{Guid.NewGuid():N}";
        await SeedActiveSubAccountAsync(otherClientCompanyId);
        using var otherClient = CreateClientFor(otherClientCompanyId);

        var response = await otherClient.GetAsync(
            $"v1/sub-accounts/{subAccountId}/transactions/{transactionId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
