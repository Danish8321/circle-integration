using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests.Admin;

public sealed class DeadLetterControllerTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private HttpClient CreateClientFor(string clientCompanyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        return client;
    }

    private async Task<WebhookInboxEntry> SeedDeadLetteredWebhookAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var entry = new WebhookInboxEntry
        {
            Id = Guid.NewGuid(),
            Topic = "paymentIntents", // no IWebhookTopicProcessor registered for this topic
            CircleEventId = $"evt-{Guid.NewGuid():N}",
            PayloadJson = "{}",
            ReceivedAtUtc = DateTime.UtcNow,
            Processed = false,
            Attempts = 5,
            LastError = "boom",
            ProcessingResult = "Failed",
        };
        dbContext.WebhookInboxEntries.Add(entry);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return entry;
    }

    private async Task<string> SeedSubAccountCallerAsync()
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        dbContext.SubAccounts.Add(SubAccount.Create(clientCompanyId, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return clientCompanyId;
    }

    private async Task<NotificationOutboxEntry> SeedDeadLetteredNotificationAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var entry = new NotificationOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = "SubAccount.Disabled",
            ClientCompanyId = $"client-{Guid.NewGuid():N}",
            EntityId = $"entity-{Guid.NewGuid():N}",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-10),
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            PayloadJson = "{}",
            Status = NotificationDeliveryStatus.Pending,
            AttemptCount = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddDays(1),
        };
        dbContext.NotificationOutboxEntries.Add(entry);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return entry;
    }

    [Fact]
    public async Task ReplayWebhookInboxEntry_AsAdmin_ReturnsOkAndResetsAttempts()
    {
        var entry = await SeedDeadLetteredWebhookAsync();
        using var admin = CreateClientFor("apiso-admin");

        var response = await admin.PostAsync(
            $"v1/admin/webhooks/{entry.Id}/replay", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReplayWebhookInboxEntryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(entry.Id, body!.WebhookInboxEntryId);
        // No processor registered for "paymentIntents", so the existing pipeline's own dispatch
        // deterministically returns Unhandled — proving replay went through WebhookProcessor's
        // real topic-processor lookup rather than a bespoke path.
        Assert.Equal(WebhookProcessingStatus.Unhandled.ToString(), body.Status);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var reloaded = await dbContext.WebhookInboxEntries.AsNoTracking()
            .FirstAsync(x => x.Id == entry.Id, TestContext.Current.CancellationToken);
        // Attempts was reset to 0 by ResetForReplayAsync then incremented once by the real
        // MarkFailedAsync call inside WebhookProcessor's dispatch.
        Assert.Equal(1, reloaded.Attempts);
        Assert.False(reloaded.Processed);
    }

    [Fact]
    public async Task ReplayWebhookInboxEntry_AsSubAccountCaller_ReturnsForbiddenProblemDetails()
    {
        var entry = await SeedDeadLetteredWebhookAsync();
        var clientCompanyId = await SeedSubAccountCallerAsync();
        using var client = CreateClientFor(clientCompanyId);

        var response = await client.PostAsync(
            $"v1/admin/webhooks/{entry.Id}/replay", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var reloaded = await dbContext.WebhookInboxEntries.AsNoTracking()
            .FirstAsync(x => x.Id == entry.Id, TestContext.Current.CancellationToken);
        Assert.Equal(5, reloaded.Attempts);
    }

    [Fact]
    public async Task ReplayNotificationOutboxEntry_AsAdmin_ReturnsOkAndResetsStateForDispatcherPickup()
    {
        var entry = await SeedDeadLetteredNotificationAsync();
        using var admin = CreateClientFor("apiso-admin");

        var response = await admin.PostAsync(
            $"v1/admin/notifications/{entry.Id}/replay", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReplayNotificationOutboxEntryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(entry.Id, body!.NotificationOutboxEntryId);
        Assert.Equal(NotificationDeliveryStatus.Pending.ToString(), body.Status);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var reloaded = await dbContext.NotificationOutboxEntries.AsNoTracking()
            .FirstAsync(x => x.Id == entry.Id, TestContext.Current.CancellationToken);
        // NotificationDispatchBackgroundService polls live in this host, so by the time we read
        // back the entry it may already have been picked up and delivered by the real
        // NotificationDispatcher (matching NotificationOutboxRepository.GetDueBatchAsync's own
        // predicate: Status == Pending && (NextAttemptAtUtc == null || NextAttemptAtUtc <= now)).
        // Either outcome proves the replay reset the dead-lettered state for real pickup.
        Assert.True(reloaded.Status is NotificationDeliveryStatus.Pending or NotificationDeliveryStatus.Delivered);
        Assert.NotEqual(5, reloaded.AttemptCount);
    }

    [Fact]
    public async Task ReplayNotificationOutboxEntry_AsSubAccountCaller_ReturnsForbiddenProblemDetails()
    {
        var entry = await SeedDeadLetteredNotificationAsync();
        var clientCompanyId = await SeedSubAccountCallerAsync();
        using var client = CreateClientFor(clientCompanyId);

        var response = await client.PostAsync(
            $"v1/admin/notifications/{entry.Id}/replay", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var reloaded = await dbContext.NotificationOutboxEntries.AsNoTracking()
            .FirstAsync(x => x.Id == entry.Id, TestContext.Current.CancellationToken);
        Assert.Equal(5, reloaded.AttemptCount);
    }
}
