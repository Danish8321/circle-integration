namespace TreasuryServiceOrchestrator.Application.Ports;

// Status is the raw provider literal, not the Domain RecipientStatus enum — mapping to the
// canonical enum is owned by RecipientStatusMapper (ticket 05.3).
public sealed record RegisteredRecipient(
    string CircleRecipientId,
    string Status);
