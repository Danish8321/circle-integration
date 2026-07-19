namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// A webhook a mock gateway scheduled for future delivery through the real webhook pipeline
/// (ADR 0007 — mock mode is a producer feeding the real inbox/processor, not a shortcut around
/// it). <see cref="DeliverAtUtc"/> is resolved at scheduling time via <see cref="TimeProvider"/>,
/// never <c>DateTime.UtcNow</c> directly (CLAUDE.md invariant 2).
/// </summary>
public sealed record ScheduledMockWebhook(string Topic, string PayloadJson, DateTime DeliverAtUtc);
