using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using TreasuryServiceOrchestrator.Api.Middleware;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Api;

public sealed class CallerIdentityMiddlewareTests
{
    private const string AdminCallerId = "apiso-admin";

    private readonly Mock<ISubAccountRepository> _subAccountRepository = new();
    private bool _nextCalled;

    [Fact]
    public async Task Missing_header_returns_401_and_does_not_call_next()
    {
        var httpContext = new DefaultHttpContext();

        await InvokeAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
        _subAccountRepository.Verify(
            r => r.GetByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Admin_caller_id_sets_admin_role_without_registry_lookup()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["ClientCompanyId"] = AdminCallerId;
        var callerContext = new HttpCallerContext();

        await InvokeAsync(httpContext, callerContext);

        _nextCalled.Should().BeTrue();
        callerContext.Role.Should().Be(CallerRole.Admin);
        callerContext.CallerId.Should().Be(AdminCallerId);
        _subAccountRepository.Verify(
            r => r.GetByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Known_sub_account_caller_id_sets_sub_account_role_and_calls_next()
    {
        const string clientCompanyId = "client-1";
        _subAccountRepository
            .Setup(r => r.GetByClientCompanyIdAsync(clientCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubAccount.Create(clientCompanyId, DateTime.UtcNow));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["ClientCompanyId"] = clientCompanyId;
        var callerContext = new HttpCallerContext();

        await InvokeAsync(httpContext, callerContext);

        _nextCalled.Should().BeTrue();
        callerContext.Role.Should().Be(CallerRole.SubAccount);
        callerContext.CallerId.Should().Be(clientCompanyId);
    }

    [Fact]
    public async Task Unregistered_caller_id_returns_401_and_does_not_call_next()
    {
        _subAccountRepository
            .Setup(r => r.GetByClientCompanyIdAsync("unknown-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["ClientCompanyId"] = "unknown-client";

        await InvokeAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    private async Task InvokeAsync(HttpContext httpContext, HttpCallerContext? callerContext = null)
    {
        var middleware = new CallerIdentityMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        });

        var options = Options.Create(new CallerIdentityOptions { AdminCallerId = AdminCallerId });

        await middleware.InvokeAsync(
            httpContext, callerContext ?? new HttpCallerContext(), options, _subAccountRepository.Object);
    }
}
