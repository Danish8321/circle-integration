using FluentAssertions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Services;

public sealed class RecipientStatusMapperTests
{
    [Theory]
    [InlineData("active", RecipientStatus.Active)]
    [InlineData("denied", RecipientStatus.Denied)]
    [InlineData("pending", RecipientStatus.PendingApproval)]
    [InlineData("inactive", RecipientStatus.PendingApproval)]
    [InlineData("pending_verification", RecipientStatus.PendingApproval)]
    [InlineData("verification_succeeded", RecipientStatus.PendingApproval)]
    public void Map_WithDocumentedLiteral_ReturnsExpectedStatus(string rawStatus, RecipientStatus expected)
    {
        var result = RecipientStatusMapper.Map(rawStatus);

        result.Should().Be(expected);
    }

    [Fact]
    public void Map_WithUnknownLiteral_FallsBackToPendingApprovalWithoutThrowing()
    {
        var act = () => RecipientStatusMapper.Map("some_future_literal");

        act.Should().NotThrow();
        RecipientStatusMapper.Map("some_future_literal").Should().Be(RecipientStatus.PendingApproval);
    }

    [Fact]
    public void Map_WithUnknownLiteral_InvokesLogCallback()
    {
        string? logged = null;

        RecipientStatusMapper.Map("some_future_literal", message => logged = message);

        logged.Should().NotBeNull();
        logged.Should().Contain("some_future_literal");
    }

    [Fact]
    public void Map_WithKnownLiteral_DoesNotInvokeLogCallback()
    {
        var invoked = false;

        RecipientStatusMapper.Map("active", _ => invoked = true);

        invoked.Should().BeFalse();
    }
}
