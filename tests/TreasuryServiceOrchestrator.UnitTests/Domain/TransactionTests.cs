
namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class TransactionTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SubAccountId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidFields_SetsAllProperties()
    {
        var amount = new Money(100m, "USDC");

        var transaction = Transaction.Create(
            SubAccountId,
            "client-1",
            TransactionType.Deposit,
            TransactionStatus.Complete,
            amount,
            "provider-ref-1",
            DepositSourceType.Wire,
            null,
            "correlation-1",
            NowUtc);

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal(SubAccountId, transaction.SubAccountId);
        Assert.Equal("client-1", transaction.ClientCompanyId);
        Assert.Equal(TransactionType.Deposit, transaction.Type);
        Assert.Equal(TransactionStatus.Complete, transaction.Status);
        Assert.Equal(amount, transaction.Amount);
        Assert.Equal("provider-ref-1", transaction.ProviderReferenceId);
        Assert.Equal(DepositSourceType.Wire, transaction.DepositSourceType);
        Assert.Null(transaction.FailureReason);
        Assert.Equal("correlation-1", transaction.CorrelationId);
        Assert.Equal(NowUtc, transaction.CreatedAtUtc);
        Assert.Equal(NowUtc, transaction.UpdatedAtUtc);
    }

    [Fact]
    public void Create_ForTransferOrRedemption_AllowsNullDepositSourceType()
    {
        var transaction = Transaction.Create(
            SubAccountId,
            "client-1",
            TransactionType.Transfer,
            TransactionStatus.Pending,
            new Money(-50m, "USDC"),
            "provider-ref-2",
            null,
            null,
            "correlation-2",
            NowUtc);

        Assert.Null(transaction.DepositSourceType);
    }

    [Fact]
    public void Create_WithFailureReason_SetsFailureReason()
    {
        var transaction = Transaction.Create(
            SubAccountId,
            "client-1",
            TransactionType.Deposit,
            TransactionStatus.Failed,
            new Money(0m, "USDC"),
            "provider-ref-3",
            DepositSourceType.OnChain,
            "currency mismatch",
            "correlation-3",
            NowUtc);

        Assert.Equal(TransactionStatus.Failed, transaction.Status);
        Assert.Equal("currency mismatch", transaction.FailureReason);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Transaction.Create(
                Guid.Empty,
                "client-1",
                TransactionType.Deposit,
                TransactionStatus.Complete,
                new Money(1m, "USDC"),
                "provider-ref-1",
                DepositSourceType.Wire,
                null,
                "correlation-1",
                NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankClientCompanyId_Throws(string clientCompanyId)
    {
        Assert.Throws<ArgumentException>(
            () => Transaction.Create(
                SubAccountId,
                clientCompanyId,
                TransactionType.Deposit,
                TransactionStatus.Complete,
                new Money(1m, "USDC"),
                "provider-ref-1",
                DepositSourceType.Wire,
                null,
                "correlation-1",
                NowUtc));
    }

    [Fact]
    public void Create_WithNullAmount_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Transaction.Create(
                SubAccountId,
                "client-1",
                TransactionType.Deposit,
                TransactionStatus.Complete,
                null!,
                "provider-ref-1",
                DepositSourceType.Wire,
                null,
                "correlation-1",
                NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankProviderReferenceId_Throws(string providerReferenceId)
    {
        Assert.Throws<ArgumentException>(
            () => Transaction.Create(
                SubAccountId,
                "client-1",
                TransactionType.Deposit,
                TransactionStatus.Complete,
                new Money(1m, "USDC"),
                providerReferenceId,
                DepositSourceType.Wire,
                null,
                "correlation-1",
                NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankCorrelationId_Throws(string correlationId)
    {
        Assert.Throws<ArgumentException>(
            () => Transaction.Create(
                SubAccountId,
                "client-1",
                TransactionType.Deposit,
                TransactionStatus.Complete,
                new Money(1m, "USDC"),
                "provider-ref-1",
                DepositSourceType.Wire,
                null,
                correlationId,
                NowUtc));
    }
}
