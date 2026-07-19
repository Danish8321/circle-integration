using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Recipients;

public sealed class GetRecipientQueryHandlerTests
{
    private readonly Mock<IRecipientRepository> recipients = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetRecipientQueryHandler handler;

    public GetRecipientQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetRecipientQueryHandler(recipients.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithExistingRecipient_ReturnsMappedResult()
    {
        var recipient = Recipient.Create(
            Guid.NewGuid(), "client-1", "ETH", "0xabc", "My wallet", "circle-recipient-1",
            RecipientStatus.Active, DateTime.UtcNow);
        recipients
            .Setup(x => x.FindByIdAsync(recipient.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);

        var result = await handler.HandleAsync(
            new GetRecipientQuery(recipient.Id), TestContext.Current.CancellationToken);

        result.Id.Should().Be(recipient.Id);
        result.Status.Should().Be(RecipientStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_WithMissingOrCrossTenantRecipient_ThrowsNotFound()
    {
        var recipientId = Guid.NewGuid();
        recipients
            .Setup(x => x.FindByIdAsync(recipientId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Recipient?)null);

        var act = () => handler.HandleAsync(
            new GetRecipientQuery(recipientId), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnidentifiedCaller_ThrowsTenantForbiddenWithoutQueryingRepository()
    {
        callerContext.Setup(x => x.CallerId).Returns(string.Empty);

        var act = () => handler.HandleAsync(
            new GetRecipientQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        recipients.Verify(
            x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
