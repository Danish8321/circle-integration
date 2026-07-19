namespace TreasuryServiceOrchestrator.Domain;

public class Recipient
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public string Chain { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public string? CircleRecipientId { get; private set; }
    public RecipientStatus Status { get; private set; }
    public string? DenialReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private Recipient()
    {
    }

    public static Recipient Create(
        Guid subAccountId,
        string clientCompanyId,
        string chain,
        string address,
        string label,
        string? circleRecipientId,
        RecipientStatus status,
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

        if (string.IsNullOrWhiteSpace(chain))
        {
            throw new ArgumentException("Chain is required.", nameof(chain));
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address is required.", nameof(address));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label is required.", nameof(label));
        }

        return new Recipient
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            ClientCompanyId = clientCompanyId,
            Chain = chain,
            Address = address,
            Label = label,
            CircleRecipientId = circleRecipientId,
            Status = status,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void UpdateStatus(RecipientStatus status, string? denialReason, DateTime nowUtc)
    {
        Status = status;
        DenialReason = denialReason;
        UpdatedAtUtc = nowUtc;
    }
}
