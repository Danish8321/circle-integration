using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
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
            .Setup(x => x.ListForSubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
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
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNoRecipients_ReturnsEmptyList()
    {
        var subAccountId = Guid.NewGuid();
        recipients
            .Setup(x => x.ListForSubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            new ListRecipientsQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }
}
