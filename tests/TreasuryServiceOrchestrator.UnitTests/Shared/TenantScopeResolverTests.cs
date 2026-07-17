using FluentAssertions;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Shared;

public sealed class TenantScopeResolverTests
{
    private sealed record FakeCallerContext(string CallerId, CallerRole Role) : ICallerContext;

    [Fact]
    public void Resolve_SubAccountCallerWithNullRequestedId_ReturnsSingleForOwnTenant()
    {
        var caller = new FakeCallerContext("client-1", CallerRole.SubAccount);

        var scope = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: null);

        scope.Should().Be(new TenantScope.SingleTenant("client-1"));
    }

    [Fact]
    public void Resolve_SubAccountCallerRequestingOwnTenant_ReturnsSingleForOwnTenant()
    {
        var caller = new FakeCallerContext("client-1", CallerRole.SubAccount);

        var scope = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: "client-1");

        scope.Should().Be(new TenantScope.SingleTenant("client-1"));
    }

    [Fact]
    public void Resolve_SubAccountCallerRequestingAnotherTenant_ThrowsTenantForbidden()
    {
        var caller = new FakeCallerContext("client-1", CallerRole.SubAccount);

        var act = () => TenantScopeResolver.Resolve(caller, requestedClientCompanyId: "client-2");

        act.Should().Throw<TenantForbiddenException>();
    }

    [Fact]
    public void Resolve_AdminCallerWithExplicitRequestedId_ReturnsSingleForThatTenant()
    {
        var caller = new FakeCallerContext("admin-1", CallerRole.Admin);

        var scope = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: "client-7");

        scope.Should().Be(new TenantScope.SingleTenant("client-7"));
    }

    [Fact]
    public void Resolve_AdminCallerWithNullRequestedId_ReturnsAllTenants()
    {
        var caller = new FakeCallerContext("admin-1", CallerRole.Admin);

        var scope = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: null);

        scope.Should().BeOfType<TenantScope.AllTenants>();
    }
}
