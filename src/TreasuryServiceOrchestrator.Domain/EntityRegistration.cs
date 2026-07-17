namespace TreasuryServiceOrchestrator.Domain;

public class EntityRegistration
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public string BusinessName { get; private set; } = string.Empty;
    public string BusinessUniqueIdentifier { get; private set; } = string.Empty;
    public string IdentifierIssuingCountryCode { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Postcode { get; private set; } = string.Empty;
    public string StreetName { get; private set; } = string.Empty;
    public string BuildingNumber { get; private set; } = string.Empty;
    public string? CircleWalletId { get; private set; }
    public EntityRegistrationStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private EntityRegistration()
    {
    }

    public static EntityRegistration Create(
        Guid subAccountId,
        string clientCompanyId,
        string businessName,
        string businessUniqueIdentifier,
        string identifierIssuingCountryCode,
        string country,
        string state,
        string city,
        string postcode,
        string streetName,
        string buildingNumber,
        string? circleWalletId,
        DateTime nowUtc)
    {
        if (subAccountId == Guid.Empty)
        {
            throw new ArgumentException("SubAccountId is required.", nameof(subAccountId));
        }

        if (string.IsNullOrWhiteSpace(clientCompanyId))
        {
            throw new ArgumentException("ClientCompanyId is required.", nameof(clientCompanyId));
        }

        if (string.IsNullOrWhiteSpace(businessName))
        {
            throw new ArgumentException("BusinessName is required.", nameof(businessName));
        }

        return new EntityRegistration
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            ClientCompanyId = clientCompanyId,
            BusinessName = businessName,
            BusinessUniqueIdentifier = businessUniqueIdentifier,
            IdentifierIssuingCountryCode = identifierIssuingCountryCode,
            Country = country,
            State = state,
            City = city,
            Postcode = postcode,
            StreetName = streetName,
            BuildingNumber = buildingNumber,
            CircleWalletId = circleWalletId,
            Status = EntityRegistrationStatus.Pending,
            RejectionReason = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void Reject(string reason, DateTime nowUtc)
    {
        if (Status != EntityRegistrationStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot reject from status {Status}; expected {EntityRegistrationStatus.Pending}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        Status = EntityRegistrationStatus.Rejected;
        RejectionReason = reason;
        UpdatedAtUtc = nowUtc;
    }

    public void Accept(DateTime nowUtc)
    {
        if (Status != EntityRegistrationStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot accept from status {Status}; expected {EntityRegistrationStatus.Pending}.");
        }

        Status = EntityRegistrationStatus.Accepted;
        UpdatedAtUtc = nowUtc;
    }
}
