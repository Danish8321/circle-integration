using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance;

public static class EntityRegistrationStatusMapper
{
    public static EntityRegistrationStatus Map(string circleComplianceState)
    {
        return circleComplianceState.Trim().ToUpperInvariant() switch
        {
            "PENDING" => EntityRegistrationStatus.Pending,
            "ACCEPTED" => EntityRegistrationStatus.Accepted,
            "REJECTED" => EntityRegistrationStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(
                nameof(circleComplianceState), circleComplianceState, "Unrecognized compliance state."),
        };
    }
}
