namespace TreasuryServiceOrchestrator.Domain.Ledger;

public class DepositAddress
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string Chain { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string? CircleAddressId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private DepositAddress()
    {
    }

    public static DepositAddress Create(
        Guid subAccountId,
        string chain,
        string currency,
        string address,
        string? circleAddressId,
        DateTime nowUtc)
    {
        if (subAccountId == Guid.Empty)
        {
            throw new ArgumentException("SubAccountId is required.", nameof(subAccountId));
        }

        if (string.IsNullOrWhiteSpace(chain))
        {
            throw new ArgumentException("Chain is required.", nameof(chain));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address is required.", nameof(address));
        }

        return new DepositAddress
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            Chain = chain,
            Currency = currency,
            Address = address,
            CircleAddressId = circleAddressId,
            CreatedAtUtc = nowUtc,
        };
    }
}
