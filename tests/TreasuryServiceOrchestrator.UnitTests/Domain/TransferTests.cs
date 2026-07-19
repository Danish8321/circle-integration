
namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class TransferTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SubAccountId = Guid.NewGuid();
    private static readonly Guid RecipientId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidFields_SetsAllPropertiesAndStartsPending()
    {
        var amount = new Money(100m, "USDC");

        var transfer = Transfer.Create(
            SubAccountId,
            "client-1",
            RecipientId,
            amount,
            "correlation-1",
            NowUtc);

        Assert.NotEqual(Guid.Empty, transfer.Id);
        Assert.Equal(SubAccountId, transfer.SubAccountId);
        Assert.Equal("client-1", transfer.ClientCompanyId);
        Assert.Equal(RecipientId, transfer.RecipientId);
        Assert.Equal(amount, transfer.Amount);
        Assert.Equal(TransferStatus.Pending, transfer.Status);
        Assert.Null(transfer.CircleTransferId);
        Assert.Null(transfer.FailureReason);
        Assert.Equal("correlation-1", transfer.CorrelationId);
        Assert.Equal(NowUtc, transfer.CreatedAtUtc);
        Assert.Equal(NowUtc, transfer.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Transfer.Create(
                Guid.Empty, "client-1", RecipientId, new Money(1m, "USDC"), "correlation-1", NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankClientCompanyId_Throws(string clientCompanyId)
    {
        Assert.Throws<ArgumentException>(
            () => Transfer.Create(
                SubAccountId, clientCompanyId, RecipientId, new Money(1m, "USDC"), "correlation-1", NowUtc));
    }

    [Fact]
    public void Create_WithEmptyRecipientId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Transfer.Create(
                SubAccountId, "client-1", Guid.Empty, new Money(1m, "USDC"), "correlation-1", NowUtc));
    }

    [Fact]
    public void Create_WithNullAmount_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Transfer.Create(
                SubAccountId, "client-1", RecipientId, null!, "correlation-1", NowUtc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveAmount_Throws(decimal amount)
    {
        Assert.Throws<ArgumentException>(
            () => Transfer.Create(
                SubAccountId, "client-1", RecipientId, new Money(amount, "USDC"), "correlation-1", NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankCorrelationId_Throws(string correlationId)
    {
        Assert.Throws<ArgumentException>(
            () => Transfer.Create(
                SubAccountId, "client-1", RecipientId, new Money(1m, "USDC"), correlationId, NowUtc));
    }

    [Fact]
    public void SetProviderReference_WithValidId_SetsCircleTransferIdAndUpdatedAtUtc()
    {
        var transfer = Transfer.Create(
            SubAccountId, "client-1", RecipientId, new Money(1m, "USDC"), "correlation-1", NowUtc);
        var later = NowUtc.AddMinutes(5);

        transfer.SetProviderReference("circle-transfer-1", later);

        Assert.Equal("circle-transfer-1", transfer.CircleTransferId);
        Assert.Equal(later, transfer.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetProviderReference_WithBlankId_Throws(string circleTransferId)
    {
        var transfer = Transfer.Create(
            SubAccountId, "client-1", RecipientId, new Money(1m, "USDC"), "correlation-1", NowUtc);

        Assert.Throws<ArgumentException>(() => transfer.SetProviderReference(circleTransferId, NowUtc));
    }

    [Fact]
    public void UpdateStatus_ToFailed_SetsStatusAndFailureReason()
    {
        var transfer = Transfer.Create(
            SubAccountId, "client-1", RecipientId, new Money(1m, "USDC"), "correlation-1", NowUtc);
        var later = NowUtc.AddMinutes(5);

        transfer.UpdateStatus(TransferStatus.Failed, "insufficient funds", later);

        Assert.Equal(TransferStatus.Failed, transfer.Status);
        Assert.Equal("insufficient funds", transfer.FailureReason);
        Assert.Equal(later, transfer.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateStatus_ToComplete_ClearsFailureReasonWhenNull()
    {
        var transfer = Transfer.Create(
            SubAccountId, "client-1", RecipientId, new Money(1m, "USDC"), "correlation-1", NowUtc);

        transfer.UpdateStatus(TransferStatus.Complete, null, NowUtc.AddMinutes(5));

        Assert.Equal(TransferStatus.Complete, transfer.Status);
        Assert.Null(transfer.FailureReason);
    }
}
