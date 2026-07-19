using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests.Webhooks;

public sealed class WebhookDedupTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private async Task<string> SeedPendingComplianceSubAccountAsync()
    {
        var walletId = $"wallet-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var subAccount = SubAccount.Create($"client-{Guid.NewGuid():N}", DateTime.UtcNow);
        subAccount.BeginCompliance(walletId);
        dbContext.SubAccounts.Add(subAccount);

        dbContext.EntityRegistrations.Add(EntityRegistration.Create(
            subAccount.Id, subAccount.ClientCompanyId, "Acme Inc", "EIN-123", "US", "US", "NY",
            "New York", "10001", "Broadway", "1", walletId, DateTime.UtcNow));

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return walletId;
    }

    private static object SnsEnvelope(string messageId, string walletId, string complianceState)
    {
        var innerMessage = JsonSerializer.Serialize(new
        {
            notificationType = "externalEntities",
            externalEntity = new
            {
                walletId,
                businessName = "Acme Inc",
                businessUniqueIdentifier = "EIN-123",
                complianceState,
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
    public async Task Receive_AcceptedDecision_ActivatesSubAccountAndReturnsOk()
    {
        var walletId = await SeedPendingComplianceSubAccountAsync();
        using var client = factory.CreateClient();
        var messageId = $"msg-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            "v1/webhooks/circle", SnsEnvelope(messageId, walletId, "ACCEPTED"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var subAccount = await dbContext.SubAccounts.FirstAsync(
            x => x.CircleWalletId == walletId, TestContext.Current.CancellationToken);
        Assert.Equal(SubAccountLifecycleState.Active, subAccount.LifecycleState);
    }

    [Fact]
    public async Task Receive_RedeliveredMessageId_IsNotReprocessed()
    {
        var walletId = await SeedPendingComplianceSubAccountAsync();
        using var client = factory.CreateClient();
        var messageId = $"msg-{Guid.NewGuid():N}";
        var envelope = SnsEnvelope(messageId, walletId, "ACCEPTED");

        var first = await client.PostAsJsonAsync(
            "v1/webhooks/circle", envelope, TestContext.Current.CancellationToken);
        var second = await client.PostAsJsonAsync(
            "v1/webhooks/circle", envelope, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var matchingEntries = await dbContext.WebhookInboxEntries
            .Where(x => x.CircleEventId == messageId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(matchingEntries);
    }

    [Fact]
    public async Task Receive_UnhandledTopic_ReturnsOkNotError()
    {
        using var client = factory.CreateClient();
        var messageId = $"msg-{Guid.NewGuid():N}";
        var innerMessage = JsonSerializer.Serialize(new { notificationType = "paymentIntents" });
        var envelope = new
        {
            Type = "Notification",
            MessageId = messageId,
            TopicArn = "arn:aws:sns:us-east-1:000000000000:test-topic",
            Message = innerMessage,
            Signature = "irrelevant-under-mock-verifier",
            SigningCertURL = "https://sns.us-east-1.amazonaws.com/cert.pem",
        };

        var response = await client.PostAsJsonAsync(
            "v1/webhooks/circle", envelope, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Receive_WithoutClientCompanyIdHeader_IsNotBlockedByTenantMiddleware()
    {
        // Confirms the CallerIdentityMiddleware bypass for /v1/webhooks/circle — no
        // ClientCompanyId header is sent, which would 401 on every other endpoint.
        var walletId = await SeedPendingComplianceSubAccountAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "v1/webhooks/circle", SnsEnvelope($"msg-{Guid.NewGuid():N}", walletId, "ACCEPTED"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
