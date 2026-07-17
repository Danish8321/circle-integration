namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class WebhookInboxEntry
{
    public Guid Id { get; set; }
    public required string Topic { get; set; }
    public required string CircleEventId { get; set; }
    public required string PayloadJson { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public bool Processed { get; set; }
    public string? ProcessingResult { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
