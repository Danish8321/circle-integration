using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests.Webhooks;

// Forces MockMode off so the real AwsSnsSignatureVerifier is wired (mirrors production DI
// branching in CircleIntegrationServiceCollectionExtensions), then exercises the rejection
// path against a forged SigningCertURL host — no real AWS network access required, since
// domain validation rejects before any cert fetch is attempted.
public sealed class WebhookSignatureVerificationTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private WebApplicationFactory<Program> RealVerifierFactory() =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["MockMode:Enabled"] = "false",
                })));

    [Fact]
    public async Task Receive_ForgedSigningCertUrl_Returns403AndWritesNoInboxRow()
    {
        using var realVerifierFactory = RealVerifierFactory();
        using var client = realVerifierFactory.CreateClient();
        var messageId = $"msg-{Guid.NewGuid():N}";
        var innerMessage = JsonSerializer.Serialize(new { notificationType = "paymentIntents" });

        var envelope = new
        {
            Type = "Notification",
            MessageId = messageId,
            TopicArn = "arn:aws:sns:us-east-1:000000000000:test-topic",
            Message = innerMessage,
            Timestamp = "2026-07-19T00:00:00Z",
            Signature = Convert.ToBase64String("not-a-real-signature"u8.ToArray()),
            SignatureVersion = "2",
            SigningCertURL = "https://evil.example.com/fake-cert.pem",
        };

        var response = await client.PostAsJsonAsync(
            "v1/webhooks/circle", envelope, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = realVerifierFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var matchingEntries = await dbContext.WebhookInboxEntries
            .Where(x => x.CircleEventId == messageId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(matchingEntries);
    }
}
