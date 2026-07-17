namespace TreasuryServiceOrchestrator.Domain;

public class LinkedBankAccount
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public string BeneficiaryName { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string RoutingNumber { get; private set; } = string.Empty;
    public string BankName { get; private set; } = string.Empty;
    public string BillingName { get; private set; } = string.Empty;
    public string BillingCity { get; private set; } = string.Empty;
    public string BillingCountry { get; private set; } = string.Empty;
    public string BillingLine1 { get; private set; } = string.Empty;
    public string BillingPostalCode { get; private set; } = string.Empty;
    public string? BillingLine2 { get; private set; }
    public string? BillingDistrict { get; private set; }
    public string BankAddressCountry { get; private set; } = string.Empty;
    public string? BankAddressBankName { get; private set; }
    public string? CircleBankAccountId { get; private set; }
    public LinkedBankAccountStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private LinkedBankAccount()
    {
    }

    public static LinkedBankAccount Create(
        Guid subAccountId,
        string clientCompanyId,
        string beneficiaryName,
        string accountNumber,
        string routingNumber,
        string bankName,
        string billingName,
        string billingCity,
        string billingCountry,
        string billingLine1,
        string billingPostalCode,
        string? billingLine2,
        string? billingDistrict,
        string bankAddressCountry,
        string? bankAddressBankName,
        DateTime nowUtc)
    {
        ValidateRequiredFields(
            subAccountId, clientCompanyId, beneficiaryName, accountNumber, routingNumber, bankName,
            billingName, billingCity, billingCountry, billingLine1, billingPostalCode, bankAddressCountry);

        return new LinkedBankAccount
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccountId,
            ClientCompanyId = clientCompanyId,
            BeneficiaryName = beneficiaryName,
            AccountNumber = accountNumber,
            RoutingNumber = routingNumber,
            BankName = bankName,
            BillingName = billingName,
            BillingCity = billingCity,
            BillingCountry = billingCountry,
            BillingLine1 = billingLine1,
            BillingPostalCode = billingPostalCode,
            BillingLine2 = billingLine2,
            BillingDistrict = billingDistrict,
            BankAddressCountry = bankAddressCountry,
            BankAddressBankName = bankAddressBankName,
            Status = LinkedBankAccountStatus.Pending,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    private static void ValidateRequiredFields(
        Guid subAccountId,
        string clientCompanyId,
        string beneficiaryName,
        string accountNumber,
        string routingNumber,
        string bankName,
        string billingName,
        string billingCity,
        string billingCountry,
        string billingLine1,
        string billingPostalCode,
        string bankAddressCountry)
    {
        if (subAccountId == Guid.Empty)
        {
            throw new ArgumentException("SubAccountId is required.", nameof(subAccountId));
        }

        if (string.IsNullOrWhiteSpace(clientCompanyId))
        {
            throw new ArgumentException("ClientCompanyId is required.", nameof(clientCompanyId));
        }

        if (string.IsNullOrWhiteSpace(beneficiaryName))
        {
            throw new ArgumentException("BeneficiaryName is required.", nameof(beneficiaryName));
        }

        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new ArgumentException("AccountNumber is required.", nameof(accountNumber));
        }

        if (string.IsNullOrWhiteSpace(routingNumber))
        {
            throw new ArgumentException("RoutingNumber is required.", nameof(routingNumber));
        }

        if (string.IsNullOrWhiteSpace(bankName))
        {
            throw new ArgumentException("BankName is required.", nameof(bankName));
        }

        if (string.IsNullOrWhiteSpace(billingName))
        {
            throw new ArgumentException("BillingName is required.", nameof(billingName));
        }

        if (string.IsNullOrWhiteSpace(billingCity))
        {
            throw new ArgumentException("BillingCity is required.", nameof(billingCity));
        }

        if (string.IsNullOrWhiteSpace(billingCountry))
        {
            throw new ArgumentException("BillingCountry is required.", nameof(billingCountry));
        }

        if (string.IsNullOrWhiteSpace(billingLine1))
        {
            throw new ArgumentException("BillingLine1 is required.", nameof(billingLine1));
        }

        if (string.IsNullOrWhiteSpace(billingPostalCode))
        {
            throw new ArgumentException("BillingPostalCode is required.", nameof(billingPostalCode));
        }

        if (string.IsNullOrWhiteSpace(bankAddressCountry))
        {
            throw new ArgumentException("BankAddressCountry is required.", nameof(bankAddressCountry));
        }
    }

    public void UpdateStatus(LinkedBankAccountStatus status, DateTime nowUtc)
    {
        Status = status;
        UpdatedAtUtc = nowUtc;
    }

    public void SetProviderReference(string circleBankAccountId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(circleBankAccountId))
        {
            throw new ArgumentException("CircleBankAccountId is required.", nameof(circleBankAccountId));
        }

        CircleBankAccountId = circleBankAccountId;
        UpdatedAtUtc = nowUtc;
    }
}
