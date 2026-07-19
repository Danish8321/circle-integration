
namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class LinkedBankAccountTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SubAccountId = Guid.NewGuid();

    private static LinkedBankAccount CreateValid() => LinkedBankAccount.Create(
        SubAccountId,
        "client-1",
        "Acme Inc",
        "123456789",
        "021000021",
        "Acme Bank",
        "Acme Inc",
        "New York",
        "US",
        "123 Main St",
        "10001",
        billingLine2: null,
        billingDistrict: null,
        bankAddressCountry: "US",
        bankAddressBankName: null,
        NowUtc);

    [Fact]
    public void Create_WithValidFields_SetsAllPropertiesAndStartsPending()
    {
        var account = CreateValid();

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal(SubAccountId, account.SubAccountId);
        Assert.Equal("client-1", account.ClientCompanyId);
        Assert.Equal("Acme Inc", account.BeneficiaryName);
        Assert.Equal("123456789", account.AccountNumber);
        Assert.Equal("021000021", account.RoutingNumber);
        Assert.Equal("Acme Bank", account.BankName);
        Assert.Equal("Acme Inc", account.BillingName);
        Assert.Equal("New York", account.BillingCity);
        Assert.Equal("US", account.BillingCountry);
        Assert.Equal("123 Main St", account.BillingLine1);
        Assert.Equal("10001", account.BillingPostalCode);
        Assert.Null(account.BillingLine2);
        Assert.Null(account.BillingDistrict);
        Assert.Equal("US", account.BankAddressCountry);
        Assert.Null(account.BankAddressBankName);
        Assert.Null(account.CircleBankAccountId);
        Assert.Equal(LinkedBankAccountStatus.Pending, account.Status);
        Assert.Equal(NowUtc, account.CreatedAtUtc);
        Assert.Equal(NowUtc, account.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            Guid.Empty, "client-1", "Acme Inc", "123456789", "021000021", "Acme Bank",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankClientCompanyId_Throws(string clientCompanyId)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, clientCompanyId, "Acme Inc", "123456789", "021000021", "Acme Bank",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBeneficiaryName_Throws(string beneficiaryName)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", beneficiaryName, "123456789", "021000021", "Acme Bank",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankAccountNumber_Throws(string accountNumber)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", accountNumber, "021000021", "Acme Bank",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankRoutingNumber_Throws(string routingNumber)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", routingNumber, "Acme Bank",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBankName_Throws(string bankName)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", "021000021", bankName,
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBillingName_Throws(string billingName)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", "021000021", "Acme Bank",
            billingName, "New York", "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBillingCity_Throws(string billingCity)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", "021000021", "Acme Bank",
            "Acme Inc", billingCity, "US", "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBillingCountry_Throws(string billingCountry)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", "021000021", "Acme Bank",
            "Acme Inc", "New York", billingCountry, "123 Main St", "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBillingLine1_Throws(string billingLine1)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", "021000021", "Acme Bank",
            "Acme Inc", "New York", "US", billingLine1, "10001", null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBillingPostalCode_Throws(string billingPostalCode)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", "021000021", "Acme Bank",
            "Acme Inc", "New York", "US", "123 Main St", billingPostalCode, null, null, "US", null, NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBankAddressCountry_Throws(string bankAddressCountry)
    {
        Assert.Throws<ArgumentException>(() => LinkedBankAccount.Create(
            SubAccountId, "client-1", "Acme Inc", "123456789", "021000021", "Acme Bank",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, null, bankAddressCountry, null, NowUtc));
    }

    [Fact]
    public void UpdateStatus_ToActive_SetsStatusAndUpdatedAtUtc()
    {
        var account = CreateValid();
        var later = NowUtc.AddMinutes(5);

        account.UpdateStatus(LinkedBankAccountStatus.Active, later);

        Assert.Equal(LinkedBankAccountStatus.Active, account.Status);
        Assert.Equal(later, account.UpdatedAtUtc);
    }

    [Fact]
    public void SetProviderReference_WithValidId_SetsCircleBankAccountIdAndUpdatedAtUtc()
    {
        var account = CreateValid();
        var later = NowUtc.AddMinutes(5);

        account.SetProviderReference("bank-account-1", later);

        Assert.Equal("bank-account-1", account.CircleBankAccountId);
        Assert.Equal(later, account.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetProviderReference_WithBlankId_Throws(string circleBankAccountId)
    {
        var account = CreateValid();

        Assert.Throws<ArgumentException>(() => account.SetProviderReference(circleBankAccountId, NowUtc));
    }
}
