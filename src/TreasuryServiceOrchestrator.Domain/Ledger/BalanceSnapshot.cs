namespace TreasuryServiceOrchestrator.Domain.Ledger;

public class BalanceSnapshot
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public Money Balance { get; private set; } = Money.Zero("USDC");
    public BalanceSnapshotReason Reason { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }

    private BalanceSnapshot()
    {
    }

    public static BalanceSnapshot Create(
        Guid subAccountId,
        string clientCompanyId,
        Money balance,
        BalanceSnapshotReason reason,
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

        ArgumentNullException.ThrowIfNull(balance);

        return new BalanceSnapshot
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            ClientCompanyId = clientCompanyId,
            Balance = balance,
            Reason = reason,
            CapturedAtUtc = nowUtc,
        };
    }
}
