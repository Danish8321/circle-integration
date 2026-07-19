using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;
using TreasuryServiceOrchestrator.TestUtilities;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public sealed class MockSubAccountGatewayTests
{
    private static CreateExternalEntityGatewayRequest CreateRequest(string businessName = "Acme Corp") =>
        new(
            BusinessName: businessName,
            BusinessUniqueIdentifier: "123456789",
            IdentifierIssuingCountryCode: "US",
            Country: "US",
            State: "NY",
            City: "New York",
            Postcode: "10001",
            StreetName: "Main St",
            BuildingNumber: "1");

    private static MockSubAccountGateway CreateSut(
        double failureInjectionRate = 0.0,
        CapturingScheduler? scheduler = null,
        string rejectSuffix = "REJECTME")
    {
        var options = Options.Create(new MockProviderOptions
        {
            FailureInjectionRate = failureInjectionRate,
            RejectBusinessNameSuffix = rejectSuffix,
            WebhookDelayMilliseconds = 200,
        });

        return new MockSubAccountGateway(
            options,
            scheduler ?? new CapturingScheduler(),
            new FixedRandomSource(fixedDouble: failureInjectionRate > 0 ? 0.0 : 0.5));
    }

    [Fact]
    public async Task CreateExternalEntityAsync_FailureInjectionRateOne_ThrowsProviderUnavailable()
    {
        var sut = CreateSut(failureInjectionRate: 1.0);

        var act = () => sut.CreateExternalEntityAsync(CreateRequest(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProviderUnavailableException>();
    }

    [Fact]
    public async Task CreateExternalEntityAsync_FailureInjectionRateZero_SucceedsAndReturnsPending()
    {
        var sut = CreateSut(failureInjectionRate: 0.0);

        var result = await sut.CreateExternalEntityAsync(CreateRequest(), TestContext.Current.CancellationToken);

        result.ComplianceState.Should().Be("Pending");
        result.WalletId.Should().NotBeNullOrWhiteSpace();
        result.BusinessName.Should().Be("Acme Corp");
        result.BusinessUniqueIdentifier.Should().Be("123456789");
    }

    [Fact]
    public async Task CreateExternalEntityAsync_NormalBusinessName_SchedulesAcceptedWebhookWithWalletId()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler: scheduler);

        var result = await sut.CreateExternalEntityAsync(CreateRequest(), TestContext.Current.CancellationToken);

        scheduler.Scheduled.Should().ContainSingle();
        var scheduled = scheduler.Scheduled[0];
        scheduled.Topic.Should().Be("externalEntities");
        scheduled.Delay.Should().Be(TimeSpan.FromMilliseconds(200));

        var envelope = JsonSerializer.Deserialize<ExternalEntityWebhookEnvelope>(scheduled.PayloadJson)!;
        envelope.ExternalEntity.WalletId.Should().Be(result.WalletId);
        envelope.ExternalEntity.ComplianceState.Should().Be("Accepted");
    }

    [Fact]
    public async Task CreateExternalEntityAsync_MagicSuffixBusinessName_SchedulesRejectedWebhook()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler: scheduler);

        await sut.CreateExternalEntityAsync(
            CreateRequest(businessName: "Acme Corp REJECTME"), TestContext.Current.CancellationToken);

        var scheduled = scheduler.Scheduled[0];
        var envelope = JsonSerializer.Deserialize<ExternalEntityWebhookEnvelope>(scheduled.PayloadJson)!;
        envelope.ExternalEntity.ComplianceState.Should().Be("Rejected");
    }

    [Fact]
    public async Task CreateExternalEntityAsync_MagicSuffixIsCaseInsensitive()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler: scheduler);

        await sut.CreateExternalEntityAsync(
            CreateRequest(businessName: "Acme Corp rejectme"), TestContext.Current.CancellationToken);

        var scheduled = scheduler.Scheduled[0];
        var envelope = JsonSerializer.Deserialize<ExternalEntityWebhookEnvelope>(scheduled.PayloadJson)!;
        envelope.ExternalEntity.ComplianceState.Should().Be("Rejected");
    }

    [Fact]
    public async Task GetExternalEntityAsync_UnknownWalletId_DefaultsToPending()
    {
        var sut = CreateSut();

        var result = await sut.GetExternalEntityAsync("unknown-wallet", TestContext.Current.CancellationToken);

        result.ComplianceState.Should().Be("Pending");
    }

    [Fact]
    public async Task GetExternalEntityAsync_KnownWalletId_ReplaysStateRecordedAtCreation()
    {
        var sut = CreateSut();
        var created = await sut.CreateExternalEntityAsync(
            CreateRequest(businessName: "Acme Corp REJECTME"), TestContext.Current.CancellationToken);

        var result = await sut.GetExternalEntityAsync(created.WalletId, TestContext.Current.CancellationToken);

        result.ComplianceState.Should().Be("Rejected");
        result.BusinessName.Should().Be("Acme Corp REJECTME");
        result.BusinessUniqueIdentifier.Should().Be("123456789");
    }

    [Fact]
    public async Task GetExternalEntityAsync_FailureInjectionRateOne_ThrowsProviderUnavailable()
    {
        var sut = CreateSut(failureInjectionRate: 1.0);

        var act = () => sut.GetExternalEntityAsync("any-wallet", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProviderUnavailableException>();
    }
}
