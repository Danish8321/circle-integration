namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <summary>
/// Webhook-driven: <c>Status</c> is the raw provider literal (webhook vocabulary
/// <c>pending | inactive | active | denied</c>), mapped through <see cref="RecipientStatusMapper"/>.
/// </summary>
public sealed record ProcessRecipientDecisionCommand(
    string CircleRecipientId,
    string Status);
