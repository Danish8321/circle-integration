using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Services;

public static class EntityRegistrationStatusMapper
{
    public static EntityRegistrationStatus Map(string circleComplianceState)
    {
        return StatusLiteral.Normalize(circleComplianceState) switch
        {
            "pending" => EntityRegistrationStatus.Pending,
            "accepted" => EntityRegistrationStatus.Accepted,
            "rejected" => EntityRegistrationStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(
                nameof(circleComplianceState), circleComplianceState, "Unrecognized compliance state."),
        };
    }
}
