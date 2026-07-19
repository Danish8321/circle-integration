
namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class RedeemRequestTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SubAccountId = Guid.NewGuid();
    private static readonly Guid LinkedBankAccountId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidFields_SetsAllPropertiesAndStartsPending()
    {
        var amount = new Money(100m, "USDC");

        var request = RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, amount, "correlation-1", NowUtc);

        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(SubAccountId, request.SubAccountId);
        Assert.Equal("client-1", request.ClientCompanyId);
        Assert.Equal(LinkedBankAccountId, request.LinkedBankAccountId);
        Assert.Equal(amount, request.GrossAmount);
        Assert.Null(request.Fees);
        Assert.Null(request.NetAmount);
        Assert.Equal(TransferStatus.Pending, request.Status);
        Assert.Null(request.CircleRedeemId);
        Assert.Null(request.FailureReason);
        Assert.Equal("correlation-1", request.CorrelationId);
        Assert.Equal(NowUtc, request.CreatedAtUtc);
        Assert.Equal(NowUtc, request.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(() => RedeemRequest.Create(
            Guid.Empty, "client-1", LinkedBankAccountId, new Money(1m, "USDC"), "correlation-1", NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankClientCompanyId_Throws(string clientCompanyId)
    {
        Assert.Throws<ArgumentException>(() => RedeemRequest.Create(
            SubAccountId, clientCompanyId, LinkedBankAccountId, new Money(1m, "USDC"), "correlation-1", NowUtc));
    }

    [Fact]
    public void Create_WithEmptyLinkedBankAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(() => RedeemRequest.Create(
            SubAccountId, "client-1", Guid.Empty, new Money(1m, "USDC"), "correlation-1", NowUtc));
    }

    [Fact]
    public void Create_WithNullGrossAmount_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, null!, "correlation-1", NowUtc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveGrossAmount_Throws(decimal amount)
    {
        Assert.Throws<ArgumentException>(() => RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(amount, "USDC"), "correlation-1", NowUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankCorrelationId_Throws(string correlationId)
    {
        Assert.Throws<ArgumentException>(() => RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(1m, "USDC"), correlationId, NowUtc));
    }

    [Fact]
    public void SetProviderReference_WithValidId_SetsCircleRedeemIdAndUpdatedAtUtc()
    {
        var request = RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(1m, "USDC"), "correlation-1", NowUtc);
        var later = NowUtc.AddMinutes(5);

        request.SetProviderReference("redeem-1", later);

        Assert.Equal("redeem-1", request.CircleRedeemId);
        Assert.Equal(later, request.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetProviderReference_WithBlankId_Throws(string circleRedeemId)
    {
        var request = RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(1m, "USDC"), "correlation-1", NowUtc);

        Assert.Throws<ArgumentException>(() => request.SetProviderReference(circleRedeemId, NowUtc));
    }

    [Fact]
    public void Settle_SetsFeesNetAmountAndStatusComplete()
    {
        var request = RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(100m, "USDC"), "correlation-1", NowUtc);
        var fees = new Money(1.5m, "USDC");
        var net = new Money(98.5m, "USDC");
        var later = NowUtc.AddMinutes(5);

        request.Settle(fees, net, later);

        Assert.Equal(fees, request.Fees);
        Assert.Equal(net, request.NetAmount);
        Assert.Equal(TransferStatus.Complete, request.Status);
        Assert.Equal(later, request.UpdatedAtUtc);
    }

    [Fact]
    public void Settle_WithNullFees_Throws()
    {
        var request = RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(100m, "USDC"), "correlation-1", NowUtc);

        Assert.Throws<ArgumentNullException>(() => request.Settle(null!, new Money(98.5m, "USDC"), NowUtc));
    }

    [Fact]
    public void Settle_WithNullNetAmount_Throws()
    {
        var request = RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(100m, "USDC"), "correlation-1", NowUtc);

        Assert.Throws<ArgumentNullException>(() => request.Settle(new Money(1.5m, "USDC"), null!, NowUtc));
    }

    [Fact]
    public void UpdateStatus_ToFailed_SetsStatusAndFailureReason()
    {
        var request = RedeemRequest.Create(
            SubAccountId, "client-1", LinkedBankAccountId, new Money(1m, "USDC"), "correlation-1", NowUtc);
        var later = NowUtc.AddMinutes(5);

        request.UpdateStatus(TransferStatus.Failed, "insufficient funds", later);

        Assert.Equal(TransferStatus.Failed, request.Status);
        Assert.Equal("insufficient funds", request.FailureReason);
        Assert.Equal(later, request.UpdatedAtUtc);
    }
}
