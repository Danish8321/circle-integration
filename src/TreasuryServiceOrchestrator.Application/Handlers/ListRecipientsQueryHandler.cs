using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class ListRecipientsQueryHandler(
    IRecipientRepository recipients,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<RegisterRecipientResult>> HandleAsync(
        ListRecipientsQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot list any sub-account's recipients.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var listed = await recipients.ListForSubAccountAsync(
            query.SubAccountId, callerContext.CallerId, query.PageRequest ?? new PageRequest(), cancellationToken);

        return listed.Select(Map).ToList();
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
