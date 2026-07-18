using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed partial class WebhookInboxRepository(
    TreasuryServiceOrchestratorDbContext dbContext, ILogger<WebhookInboxRepository> logger)
    : IWebhookInboxRepository
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "WebhookInboxEntry {EntryId} (topic {Topic}, event {CircleEventId}) dead-lettered "
            + "after {Attempts} attempts. Last error: {LastError}")]
    private partial void LogDeadLettered(
        Guid entryId, string topic, string circleEventId, int attempts, string? lastError);

    public async Task<bool> TryAddAsync(WebhookInboxEntry entry, CancellationToken cancellationToken = default)
    {
        await dbContext.WebhookInboxEntries.AddAsync(entry, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Unique-constraint violation on CircleEventId — this delivery id was already seen.
            dbContext.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.WebhookInboxEntries.FirstAsync(x => x.Id == id, cancellationToken);
        entry.Processed = true;
        entry.ProcessingResult = "Processed";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.WebhookInboxEntries.FirstAsync(x => x.Id == id, cancellationToken);
        entry.Attempts += 1;
        entry.ProcessingResult = "Failed";
        entry.LastError = error;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (entry.IsDeadLettered())
        {
            LogDeadLettered(entry.Id, entry.Topic, entry.CircleEventId, entry.Attempts, entry.LastError);
        }
    }

    public async Task<WebhookInboxEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.WebhookInboxEntries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task ResetForReplayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.WebhookInboxEntries.FirstAsync(x => x.Id == id, cancellationToken);
        entry.Attempts = 0;
        entry.Processed = false;
        entry.ProcessingResult = null;
        entry.LastError = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
