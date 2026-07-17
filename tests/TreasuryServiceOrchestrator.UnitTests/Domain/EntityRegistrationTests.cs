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
}
