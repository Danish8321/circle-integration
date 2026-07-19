using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class GetRecipientQueryHandler(
    IRecipientRepository recipients,
    ICallerContext callerContext)
{
    public async Task<RegisterRecipientResult> HandleAsync(
        GetRecipientQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot look up any recipient.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var recipient = await recipients.FindByIdAsync(
            query.RecipientId, callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No recipient '{query.RecipientId}'.");

        return Map(recipient);
    }

    private static RegisterRecipientResult Map(Recipient recipient) => new(
        recipient.Id,
        recipient.SubAccountId,
        recipient.Chain,
        recipient.Address,
        recipient.Label,
        recipient.CircleRecipientId,
        recipient.Status,
        recipient.CreatedAtUtc);
}
