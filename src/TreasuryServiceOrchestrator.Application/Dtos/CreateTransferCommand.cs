using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <remarks>
/// ClientCompanyId/tenant scope is not a command field — it comes from
/// <c>ICallerContext</c> inside the handler (CLAUDE.md invariant 7). No Travel Rule
/// originator name/address fields exist here or ever should (CLAUDE.md invariant 12) —
/// the provider's create-transfer endpoint carries no such fields.
/// </remarks>
public sealed record CreateTransferCommand(
    Guid RecipientId,
    Money Amount,
    string IdempotencyKey,
    string CorrelationId);
