namespace TreasuryServiceOrchestrator.Domain;

public class Transaction
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public TransactionType Type { get; private set; }
    public TransactionStatus Status { get; private set; }
    public Money Amount { get; private set; } = Money.Zero("USDC");
    public string ProviderReferenceId { get; private set; } = string.Empty;
    public DepositSourceType? DepositSourceType { get; private set; }
    public string? FailureReason { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private Transaction()
    {
    }

    public static Transaction Create(
        Guid subAccountId,
        string clientCompanyId,
        TransactionType type,
        TransactionStatus status,
        Money amount,
        string providerReferenceId,
        DepositSourceType? depositSourceType,
        string? failureReason,
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

        ArgumentNullException.ThrowIfNull(amount);

        if (string.IsNullOrWhiteSpace(providerReferenceId))
        {
            throw new ArgumentException("ProviderReferenceId is required.", nameof(providerReferenceId));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));
        }

        return new Transaction
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            ClientCompanyId = clientCompanyId,
            Type = type,
            Status = status,
            Amount = amount,
            ProviderReferenceId = providerReferenceId,
            DepositSourceType = depositSourceType,
            FailureReason = failureReason,
            CorrelationId = correlationId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }
}
