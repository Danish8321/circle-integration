using FluentAssertions;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Transfers;

public sealed class TransferStatusMapperTests
{
    [Theory]
    [InlineData("pending", TransferStatus.Pending)]
    [InlineData("running", TransferStatus.Pending)]
    [InlineData("complete", TransferStatus.Complete)]
    [InlineData("failed", TransferStatus.Failed)]
    public void Map_WithDocumentedLiteral_ReturnsExpectedStatus(string rawStatus, TransferStatus expected)
    {
        var result = TransferStatusMapper.Map(rawStatus);

        result.Should().Be(expected);
    }

    [Fact]
    public void Map_WithUnknownLiteral_FallsBackToPendingWithoutThrowing()
    {
        var act = () => TransferStatusMapper.Map("some_future_literal");

        act.Should().NotThrow();
        TransferStatusMapper.Map("some_future_literal").Should().Be(TransferStatus.Pending);
    }

    [Fact]
    public void Map_WithUnknownLiteral_InvokesLogCallback()
    {
        string? logged = null;

        TransferStatusMapper.Map("some_future_literal", message => logged = message);

        logged.Should().NotBeNull();
        logged.Should().Contain("some_future_literal");
    }

    [Fact]
    public void Map_WithKnownLiteral_DoesNotInvokeLogCallback()
    {
        var invoked = false;

        TransferStatusMapper.Map("complete", _ => invoked = true);

        invoked.Should().BeFalse();
    }
}
