namespace TreasuryServiceOrchestrator.Domain;

// Reuses TransferStatus (Pending/Complete/Failed) rather than a redemption-specific enum —
// same three-value-status convention as Transaction/Transfer, per
// docs/features/11-redemption-and-payouts.md §2.1/§9.
public class RedeemRequest
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public Guid LinkedBankAccountId { get; private set; }
    public Money GrossAmount { get; private set; } = Money.Zero("USDC");
    public Money? Fees { get; private set; }
    public Money? NetAmount { get; private set; }
    public TransferStatus Status { get; private set; }
    public string? CircleRedeemId { get; private set; }
    public string? FailureReason { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private RedeemRequest()
    {
    }

    public static RedeemRequest Create(
        Guid subAccountId,
        string clientCompanyId,
        Guid linkedBankAccountId,
        Money grossAmount,
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

        if (linkedBankAccountId == Guid.Empty)
        {
            throw new ArgumentException("LinkedBankAccountId is required.", nameof(linkedBankAccountId));
        }

        ArgumentNullException.ThrowIfNull(grossAmount);

        if (grossAmount.Amount <= 0m)
        {
            throw new ArgumentException("GrossAmount must be positive.", nameof(grossAmount));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));
        }

        return new RedeemRequest
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            ClientCompanyId = clientCompanyId,
            LinkedBankAccountId = linkedBankAccountId,
            GrossAmount = grossAmount,
            Status = TransferStatus.Pending,
            CorrelationId = correlationId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void SetProviderReference(string circleRedeemId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(circleRedeemId))
        {
            throw new ArgumentException("CircleRedeemId is required.", nameof(circleRedeemId));
        }

        CircleRedeemId = circleRedeemId;
        UpdatedAtUtc = nowUtc;
    }

    public void Settle(Money fees, Money netAmount, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(fees);
        ArgumentNullException.ThrowIfNull(netAmount);

        Fees = fees;
        NetAmount = netAmount;
        Status = TransferStatus.Complete;
        UpdatedAtUtc = nowUtc;
    }

    public void UpdateStatus(TransferStatus status, string? failureReason, DateTime nowUtc)
    {
        Status = status;
        FailureReason = failureReason;
        UpdatedAtUtc = nowUtc;
    }
}
