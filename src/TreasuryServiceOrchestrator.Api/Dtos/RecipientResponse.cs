using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record RecipientResponse(
    Guid Id,
    Guid SubAccountId,
    string Chain,
    string Address,
    string Label,
    string? CircleRecipientId,
    RecipientStatus Status,
    DateTime CreatedAtUtc)
{
    public static RecipientResponse Map(RegisterRecipientResult result) => new(
        result.Id,
        result.SubAccountId,
        result.Chain,
        result.Address,
        result.Label,
        result.CircleRecipientId,
        result.Status,
        result.CreatedAtUtc);
}
