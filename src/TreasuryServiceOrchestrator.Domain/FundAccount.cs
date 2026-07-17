namespace TreasuryServiceOrchestrator.Domain;

public class FundAccount
{
    public Guid Id { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public Money Balance { get; private set; } = Money.Zero("USDC");
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private FundAccount()
    {
    }

    public static FundAccount Create(string clientCompanyId, Money balance, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(clientCompanyId))
        {
            throw new ArgumentException("ClientCompanyId is required.", nameof(clientCompanyId));
        }

        ArgumentNullException.ThrowIfNull(balance);

        return new FundAccount
        {
            Id = Guid.NewGuid(),
            ClientCompanyId = clientCompanyId,
            Balance = balance,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void ApplyBalance(Money balance, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(balance);

        Balance = balance;
        UpdatedAtUtc = nowUtc;
    }
}
