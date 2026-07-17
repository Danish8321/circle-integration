namespace TreasuryServiceOrchestrator.Domain;

public class Transfer
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public Guid RecipientId { get; private set; }
    public Money Amount { get; private set; } = Money.Zero("USDC");
    public TransferStatus Status { get; private set; }
    public string? CircleTransferId { get; private set; }
    public string? FailureReason { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private Transfer()
    {
    }

    public static Transfer Create(
        Guid subAccountId,
        string clientCompanyId,
        Guid recipientId,
        Money amount,
        string correlationId,
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

        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("RecipientId is required.", nameof(recipientId));
        }

        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Amount must be positive.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));
        }

        return new Transfer
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            ClientCompanyId = clientCompanyId,
            RecipientId = recipientId,
            Amount = amount,
            Status = TransferStatus.Pending,
            CorrelationId = correlationId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void SetProviderReference(string circleTransferId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(circleTransferId))
        {
            throw new ArgumentException("CircleTransferId is required.", nameof(circleTransferId));
        }

        CircleTransferId = circleTransferId;
        UpdatedAtUtc = nowUtc;
    }

    public void UpdateStatus(TransferStatus status, string? failureReason, DateTime nowUtc)
    {
        Status = status;
        FailureReason = failureReason;
        UpdatedAtUtc = nowUtc;
    }
}
