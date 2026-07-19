using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class EntityRegistrationTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static EntityRegistration CreateValid(Guid? subAccountId = null, string? businessName = null) =>
        EntityRegistration.Create(
            subAccountId ?? Guid.NewGuid(),
            clientCompanyId: "client-1",
            businessName: businessName ?? "Acme Inc",
            businessUniqueIdentifier: "EIN-123",
            identifierIssuingCountryCode: "US",
            country: "US",
            state: "NY",
            city: "New York",
            postcode: "10001",
            streetName: "Broadway",
            buildingNumber: "1",
            circleWalletId: "wallet-123",
            nowUtc: NowUtc);

    [Fact]
    public void Create_WithValidFields_StartsPendingWithNoRejectionReason()
    {
        var registration = CreateValid();

        Assert.Equal(EntityRegistrationStatus.Pending, registration.Status);
        Assert.Null(registration.RejectionReason);
        Assert.Equal("wallet-123", registration.CircleWalletId);
    }

    [Fact]
    public void Create_WithEmptySubAccountId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(subAccountId: Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBusinessName_Throws(string businessName)
    {
        Assert.Throws<ArgumentException>(() => CreateValid(businessName: businessName));
    }

    [Fact]
    public void Reject_FromPending_SetsRejectedStatusReasonAndUpdatedAt()
    {
        var registration = CreateValid();
        var laterUtc = NowUtc.AddHours(1);

        registration.Reject("Documents illegible.", laterUtc);

        Assert.Equal(EntityRegistrationStatus.Rejected, registration.Status);
        Assert.Equal("Documents illegible.", registration.RejectionReason);
        Assert.Equal(laterUtc, registration.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithBlankReason_Throws(string reason)
    {
        var registration = CreateValid();

        Assert.Throws<ArgumentException>(() => registration.Reject(reason, NowUtc));
    }

    [Fact]
    public void Reject_WhenAlreadyRejected_Throws()
    {
        var registration = CreateValid();
        registration.Reject("Documents illegible.", NowUtc);

        Assert.Throws<InvalidOperationException>(() => registration.Reject("Again.", NowUtc));
    }

    [Fact]
    public void Accept_FromPending_SetsAcceptedStatusAndUpdatedAt()
    {
        var registration = CreateValid();
        var laterUtc = NowUtc.AddHours(1);

        registration.Accept(laterUtc);

        Assert.Equal(EntityRegistrationStatus.Accepted, registration.Status);
        Assert.Equal(laterUtc, registration.UpdatedAtUtc);
    }

    [Fact]
    public void Accept_WhenAlreadyAccepted_Throws()
    {
        var registration = CreateValid();
        registration.Accept(NowUtc);

        Assert.Throws<InvalidOperationException>(() => registration.Accept(NowUtc));
    }

    [Fact]
    public void Accept_WhenAlreadyRejected_Throws()
    {
        var registration = CreateValid();
        registration.Reject("Documents illegible.", NowUtc);

        Assert.Throws<InvalidOperationException>(() => registration.Accept(NowUtc));
    }
}
