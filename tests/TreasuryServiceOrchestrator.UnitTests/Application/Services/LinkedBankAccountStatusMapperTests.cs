using FluentAssertions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Services;

public sealed class LinkedBankAccountStatusMapperTests
{
    [Theory]
    [InlineData("pending", LinkedBankAccountStatus.Pending)]
    [InlineData("complete", LinkedBankAccountStatus.Active)]
    [InlineData("failed", LinkedBankAccountStatus.Failed)]
    public void Map_WithDocumentedLiteral_ReturnsExpectedStatus(string rawStatus, LinkedBankAccountStatus expected)
    {
        var result = LinkedBankAccountStatusMapper.Map(rawStatus);

        result.Should().Be(expected);
    }

    [Fact]
    public void Map_WithUnrecognizedLiteral_Throws()
    {
        var act = () => LinkedBankAccountStatusMapper.Map("some_future_literal");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
