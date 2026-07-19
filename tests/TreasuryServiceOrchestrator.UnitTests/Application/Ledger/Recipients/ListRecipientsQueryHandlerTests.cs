using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Recipients;

public sealed class ListRecipientsQueryHandlerTests
{
    private readonly Mock<IRecipientRepository> recipients = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ListRecipientsQueryHandler handler;

    public ListRecipientsQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new ListRecipientsQueryHandler(recipients.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithRecipientsForSubAccount_ReturnsMappedResults()
    {
        var subAccountId = Guid.NewGuid();
        var recipient = Recipient.Create(
            subAccountId, "client-1", "ETH", "0xabc", "My wallet", "circle-recipient-1",
            RecipientStatus.Active, DateTime.UtcNow);
        recipients
            .Setup(x => x.ListForSubAccountAsync(
                subAccountId, "client-1", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([recipient]);

        var results = await handler.HandleAsync(
            new ListRecipientsQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        results[0].Id.Should().Be(recipient.Id);
        results[0].SubAccountId.Should().Be(subAccountId);
        results[0].Status.Should().Be(RecipientStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_WithUnidentifiedCaller_ThrowsTenantForbiddenWithoutQueryingRepository()
    {
        callerContext.Setup(x => x.CallerId).Returns(string.Empty);

        var act = () => handler.HandleAsync(
            new ListRecipientsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        recipients.Verify(
            x => x.ListForSubAccountAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNoRecipients_ReturnsEmptyList()
    {
        var subAccountId = Guid.NewGuid();
        recipients
            .Setup(x => x.ListForSubAccountAsync(
                subAccountId, "client-1", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            new ListRecipientsQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithPage2_ReturnsNextSliceNotDuplicateOfPage1AndPageSizeBoundsCount()
    {
        var subAccountId = Guid.NewGuid();
        var page1Recipient = Recipient.Create(
            subAccountId, "client-1", "ETH", "0xpage1", "Page1", "circle-recipient-page1",
            RecipientStatus.Active, DateTime.UtcNow);
        var page2Recipient = Recipient.Create(
            subAccountId, "client-1", "ETH", "0xpage2", "Page2", "circle-recipient-page2",
            RecipientStatus.Active, DateTime.UtcNow);

        recipients
            .Setup(x => x.ListForSubAccountAsync(
                subAccountId, "client-1", new PageRequest(1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page1Recipient]);
        recipients
            .Setup(x => x.ListForSubAccountAsync(
                subAccountId, "client-1", new PageRequest(2, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page2Recipient]);

        var page1Results = await handler.HandleAsync(
            new ListRecipientsQuery(subAccountId, new PageRequest(1, 1)), TestContext.Current.CancellationToken);
        var page2Results = await handler.HandleAsync(
            new ListRecipientsQuery(subAccountId, new PageRequest(2, 1)), TestContext.Current.CancellationToken);

        page1Results.Should().HaveCount(1);
        page2Results.Should().HaveCount(1);
        page1Results[0].Id.Should().Be(page1Recipient.Id);
        page2Results[0].Id.Should().Be(page2Recipient.Id);
        page2Results[0].Id.Should().NotBe(page1Results[0].Id);
    }
}
